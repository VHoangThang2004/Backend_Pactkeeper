using GameInventoryApi.Models;
using Microsoft.Extensions.Options;

namespace GameInventoryApi.Services;

public class MatchmakingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServerConfig _serverConfig;
    private readonly HashSet<int> _activePorts = new();

    public MatchmakingService(IServiceScopeFactory scopeFactory, IOptions<ServerConfig> serverConfig)
    {
        _scopeFactory = scopeFactory;
        _serverConfig = serverConfig.Value;
    }

    // -------------------------------------------------------
    // Port Management
    // -------------------------------------------------------

    // Ports are taken from the matchmaking loop and from request threads (story/friendly) at the same time
    private readonly object _portLock = new();

    private int GetNextAvailablePort()
    {
        lock (_portLock)
        {
            foreach (var port in _serverConfig.GamePorts)
            {
                if (_activePorts.Contains(port)) continue;
                // Our bookkeeping can be wrong (orphan server from before a backend restart, failed kill):
                // ask the OS too, or the new server fails to bind and the match is dead on arrival.
                if (!IsUdpPortFree(port))
                {
                    Console.WriteLine($"[Matchmaking] Port {port} is free in bookkeeping but bound by another process — skipping.");
                    continue;
                }
                _activePorts.Add(port);
                return port;
            }
            return -1;
        }
    }

    private static bool IsUdpPortFree(int port)
    {
        try
        {
            using var probe = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
            return true;
        }
        catch (System.Net.Sockets.SocketException) { return false; }
    }

    public void ReleasePort(int port)
    {
        lock (_portLock) _activePorts.Remove(port);
        Console.WriteLine($"[Matchmaking] Released port {port}");
    }

    private void MarkPortInUse(int port)
    {
        lock (_portLock) _activePorts.Add(port);
    }

    // Is this match's own server still running? On Linux the PID's command line must contain the matchId
    // (every server is spawned with "-matchId <id>"), so a recycled PID never counts. Process names are
    // useless there (truncated to 15 chars: "SRPG_SERVER.x86"). Anything we can't confirm counts as RUNNING:
    // wrongly calling a live server dead frees its port while it still holds it.
    public static bool IsMatchServerRunning(int processId, string matchId)
    {
        if (processId <= 0) return false;
        if (OperatingSystem.IsLinux())
        {
            try { return File.ReadAllText($"/proc/{processId}/cmdline").Contains(matchId); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] Can't check PID {processId} ({ex.Message}) — assuming running.");
                return true;
            }
        }
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; } // no such process
        catch { return true; }
    }

    // -------------------------------------------------------
    // Reaper — one-sided.
    //   RECORDS are removed only by: the game server's report, admin end-all, or the hard age limit.
    //   A missing process is NOT a reason to drop a record: anything else sharing the DB (another backend)
    //   can't see our processes and would wrongly call live matches dead.
    //   PROCESSES on this machine are killed when they have no record (orphans) — see KillOrphanServers.
    // -------------------------------------------------------

    private static readonly TimeSpan MaxMatchAge = TimeSpan.FromMinutes(60);

    // PIDs seen without a record on the previous sweep — killed only if still orphaned on the next one,
    // so a single odd DB read can never take a live server down
    private readonly HashSet<int> _orphanSuspects = new();

    private async Task ReapDeadMatches()
    {
        using var scope = _scopeFactory.CreateScope();
        var matchService = scope.ServiceProvider.GetRequiredService<IMatchSessionService>();

        // Throws if the DB is unreachable → the loop catches it and nothing is killed this round
        var matches = await matchService.GetAllAsync();

        foreach (var match in matches)
        {
            var age = DateTime.UtcNow - match.CreatedAt;
            if (match.Status == "stopping")
                await matchService.TerminateMatchAsync(match, "retrying server stop");
            else if (age > MaxMatchAge)
                await matchService.TerminateMatchAsync(match, $"exceeded {MaxMatchAge.TotalMinutes} min limit");
            else
            {
                MarkPortInUse(match.ServerPort); // after a backend restart, live matches still own their ports
                WarnIfSuspicious(match, age);
            }
        }
        _warnedMatches.IntersectWith(matches.Select(m => m.MatchId)); // forget ended matches

        // A server can't outlive the limit either: its record is terminated above (which kills it),
        // and once the record is gone the process is an orphan.
        KillOrphanServers(matches.Select(m => m.MatchId).ToHashSet());
    }

    // Diagnostics only — never acts. One warning per match, so the log shows problems without spamming.
    private readonly HashSet<string> _warnedMatches = new();
    private static readonly TimeSpan SuspiciousAfter = TimeSpan.FromSeconds(60);

    private void WarnIfSuspicious(MatchSession match, TimeSpan age)
    {
        if (age < SuspiciousAfter || _warnedMatches.Contains(match.MatchId)) return;

        string? problem = null;
        if (match.ProcessId <= 0)
            problem = "has no server process (ProcessId<=0) — not spawned by this backend? " +
                      "Possibly created by ANOTHER backend sharing the database";
        else if (!IsMatchServerRunning(match.ProcessId, match.MatchId))
            problem = $"server PID={match.ProcessId} is not running on this host — crashed, or the record belongs to " +
                      "another backend. Record kept; it ends on report, admin end-all, or the age limit";

        if (problem == null) return;
        _warnedMatches.Add(match.MatchId);
        Console.WriteLine($"[Reaper] WARNING match {match.MatchId} mode={match.Mode} status={match.Status} " +
                          $"port={match.ServerPort} age={age.TotalSeconds:0}s {problem}.");
    }

    private void KillOrphanServers(HashSet<string> recordedMatchIds)
    {
        var suspectsNow = new HashSet<int>();
        foreach (var (pid, matchId) in FindLocalMatchServers())
        {
            if (recordedMatchIds.Contains(matchId)) continue;

            if (!_orphanSuspects.Contains(pid))
            {
                suspectsNow.Add(pid); // first sighting — confirm next sweep
                continue;
            }

            Console.WriteLine($"[Matchmaking] Orphan server PID={pid} (match {matchId}) has no match record — killing.");
            if (!StopMatchServer(pid, matchId))
                suspectsNow.Add(pid); // kill failed — retry next sweep
        }
        _orphanSuspects.Clear();
        _orphanSuspects.UnionWith(suspectsNow);
    }

    // Game servers running on THIS machine, from /proc: argv[0] is our server exe, followed by "-matchId <id>".
    // Linux only — on Windows (local dev) there is no portable cmdline access, so no orphan sweep there.
    private List<(int Pid, string MatchId)> FindLocalMatchServers()
    {
        var found = new List<(int, string)>();
        var exeName = Path.GetFileName(_serverConfig.UnityServerExePath);
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(exeName)) return found;

        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid)) continue;

            string[] args;
            try { args = File.ReadAllText(Path.Combine(dir, "cmdline")).Split('\0'); }
            catch { continue; } // exited meanwhile, or not readable

            if (args.Length < 2 || Path.GetFileName(args[0]) != exeName) continue;

            int i = Array.IndexOf(args, "-matchId");
            if (i >= 0 && i + 1 < args.Length) found.Add((pid, args[i + 1]));
        }
        return found;
    }

    // Stops this match's server if it is still running (only that exact process — see IsMatchServerRunning).
    // Returns true once it is confirmed gone, i.e. its UDP port is really free; false if it is still running.
    public bool StopMatchServer(int processId, string matchId)
    {
        if (!IsMatchServerRunning(processId, matchId)) return true;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(5000);
            Console.WriteLine($"[Matchmaking] Killed server process {processId} (match {matchId})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Matchmaking] Failed to kill process {processId}: {ex.Message}");
        }
        return !IsMatchServerRunning(processId, matchId);
    }

    // -------------------------------------------------------
    // PvP Background Loop
    // -------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Host + PID make it obvious in logs which backend instance did what (several may share one DB)
        Console.WriteLine($"[Matchmaking] Background service started on host={Environment.MachineName} " +
                          $"pid={Environment.ProcessId} exe='{_serverConfig.UnityServerExePath}' " +
                          $"ports=[{string.Join(",", _serverConfig.GamePorts)}] matchLimit={MaxMatchAge.TotalMinutes}min");
        while (!stoppingToken.IsCancellationRequested)
        {
            // An exception escaping here would stop matchmaking for good
            try
            {
                await ReapDeadMatches();
                await TryMatchPlayers();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] Loop error: {ex}");
            }
            await Task.Delay(3000, stoppingToken);
        }
    }

    private async Task TryMatchPlayers()
    {
        using var scope = _scopeFactory.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IMatchQueueService>();
        var matchService = scope.ServiceProvider.GetRequiredService<IMatchSessionService>();

        var waiting = await queueService.GetWaitingPlayersAsync();
        if (waiting.Count < 2)
        {
            return;
        }

        var pvpWaiting = waiting.Where(e => e.Mode != "ranked").ToList();
        var rankedWaiting = waiting.Where(e => e.Mode == "ranked").ToList();

        if (pvpWaiting.Count >= 2)
        {
            await TryMatchPvpPlayers(queueService, matchService, pvpWaiting);
        }

        if (rankedWaiting.Count >= 2)
        {
            await TryMatchRankedPlayers(queueService, matchService, rankedWaiting);
        }
    }

    private async Task TryMatchPvpPlayers(
        IMatchQueueService queueService,
        IMatchSessionService matchService,
        List<MatchQueueEntry> pvpWaiting)
    {
        var player1 = pvpWaiting[0];
        var player2 = pvpWaiting[1];

        var p1Ongoing = await matchService.GetActiveMatchByPlayerIdAsync(player1.PlayerId);
        var p2Ongoing = await matchService.GetActiveMatchByPlayerIdAsync(player2.PlayerId);
        if (p1Ongoing != null || p2Ongoing != null)
        {
            Console.WriteLine("[Matchmaking] Skipping PvP pair — player already in an ongoing match.");
            return;
        }

        int port = GetNextAvailablePort();
        if (port == -1)
        {
            Console.WriteLine("[Matchmaking] No available ports for PvP match.");
            return;
        }

        Console.WriteLine($"[Matchmaking] Pairing PvP: {player1.PlayerId} vs {player2.PlayerId}");

        var match = await matchService.CreateMatchAsync(
            player1.PlayerId, player2.PlayerId,
            _serverConfig.ServerIp, port, "pvp", "MD_PVP_001");

        int processId = SpawnServer(match.MatchId, port, "pvp", chapterId: 0, sceneId: 0);
        if (processId <= 0)
        {
            // No server = nothing can ever report or end this match — drop it now instead of leaving a ghost.
            // Players stay "waiting" in the queue, so the next loop retries the pairing.
            await matchService.DeleteAsync(match.MatchId);
            ReleasePort(port);
            Console.WriteLine($"[Matchmaking] ERROR PvP match {match.MatchId} dropped — server failed to spawn " +
                              $"(exe='{_serverConfig.UnityServerExePath}'). Players left in queue.");
            return;
        }
        await matchService.UpdateProcessIdAsync(match.MatchId, processId);

        await queueService.UpdateMatchedAsync(player1.PlayerId, match.MatchId, _serverConfig.ServerIp, port);
        await queueService.UpdateMatchedAsync(player2.PlayerId, match.MatchId, _serverConfig.ServerIp, port);

        Console.WriteLine($"[Matchmaking] PvP Match {match.MatchId} created, server PID={processId}");
    }

    private async Task TryMatchRankedPlayers(
        IMatchQueueService queueService,
        IMatchSessionService matchService,
        List<MatchQueueEntry> rankedWaiting)
    {
        // Order by oldest in queue first
        var sorted = rankedWaiting.OrderBy(e => e.JoinedAt).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var p1 = sorted[i];
            int waitSeconds = Math.Max(0, (int)(DateTime.UtcNow - p1.JoinedAt).TotalSeconds);
            // Window expands: base 100 LP gap, +50 LP for every 10 seconds waiting
            int allowedGap = 100 + (waitSeconds / 10) * 50;

            // Find closest candidate within allowed gap
            MatchQueueEntry? bestOpponent = null;
            int smallestDiff = int.MaxValue;

            for (int j = i + 1; j < sorted.Count; j++)
            {
                var p2 = sorted[j];
                int diff = Math.Abs(p1.Rating - p2.Rating);
                if (diff <= allowedGap && diff < smallestDiff)
                {
                    smallestDiff = diff;
                    bestOpponent = p2;
                }
            }

            if (bestOpponent != null)
            {
                var p1Ongoing = await matchService.GetActiveMatchByPlayerIdAsync(p1.PlayerId);
                var p2Ongoing = await matchService.GetActiveMatchByPlayerIdAsync(bestOpponent.PlayerId);
                if (p1Ongoing != null || p2Ongoing != null)
                {
                    Console.WriteLine("[Matchmaking] Skipping Ranked pair — player already in an ongoing match.");
                    continue;
                }

                int port = GetNextAvailablePort();
                if (port == -1)
                {
                    Console.WriteLine("[Matchmaking] No available ports for Ranked match.");
                    return;
                }

                Console.WriteLine($"[Matchmaking] Pairing Ranked: {p1.PlayerId} (Rating {p1.Rating}) vs {bestOpponent.PlayerId} (Rating {bestOpponent.Rating}) [diff={smallestDiff}]");

                var match = await matchService.CreateMatchAsync(
                    p1.PlayerId, bestOpponent.PlayerId,
                    _serverConfig.ServerIp, port, "ranked", "MD_PVP_001");

                int processId = SpawnServer(match.MatchId, port, "ranked", chapterId: 0, sceneId: 0);
                if (processId <= 0)
                {
                    await matchService.DeleteAsync(match.MatchId);
                    ReleasePort(port);
                    Console.WriteLine($"[Matchmaking] ERROR Ranked match {match.MatchId} dropped — server failed to spawn " +
                                      $"(exe='{_serverConfig.UnityServerExePath}'). Players left in queue.");
                    return;
                }
                await matchService.UpdateProcessIdAsync(match.MatchId, processId);

                await queueService.UpdateMatchedAsync(p1.PlayerId, match.MatchId, _serverConfig.ServerIp, port);
                await queueService.UpdateMatchedAsync(bestOpponent.PlayerId, match.MatchId, _serverConfig.ServerIp, port);

                Console.WriteLine($"[Matchmaking] Ranked Match {match.MatchId} created, server PID={processId}");
                break; // Handled one match this cycle
            }
        }
    }

    // -------------------------------------------------------
    // Story Match — called on-demand from StoryController
    // -------------------------------------------------------

    public async Task<MatchSession?> CreateStoryMatchAsync(
        IMatchSessionService matchService,
        string playerId,
        int chapterId,
        int sceneId,
        string? sceneMapId = null)
    {
        int port = GetNextAvailablePort();
        if (port == -1)
        {
            Console.WriteLine("[Matchmaking] No available ports for story match.");
            return null;
        }

        var mapId = !string.IsNullOrEmpty(sceneMapId)
            ? sceneMapId
            : GetMapIdForScene(chapterId, sceneId);

        var match = await matchService.CreateMatchAsync(
            player1Id: playerId,
            player2Id: "AI",
            serverIp: _serverConfig.ServerIp,
            port: port,
            mode: "story",
            mapId: mapId,
            chapterId: chapterId,
            sceneId: sceneId
        );

        int processId = SpawnServer(match.MatchId, port, "story", chapterId, sceneId);

        if (processId <= 0)
        {
            Console.WriteLine($"[Matchmaking] Failed to spawn story server for match {match.MatchId}");
            await matchService.DeleteAsync(match.MatchId);
            ReleasePort(port);
            return null;
        }

        await matchService.UpdateProcessIdAsync(match.MatchId, processId);

        Console.WriteLine($"[Matchmaking] Story match {match.MatchId} created " +
                          $"chapter={chapterId} scene={sceneId} PID={processId}");
        return match;
    }

    // -------------------------------------------------------
    // Friendly Match — called on-demand from LobbyHub / FriendsController
    // -------------------------------------------------------

    public async Task<MatchSession?> CreateFriendlyMatchAsync(
        IMatchSessionService matchService,
        string player1Id,
        string player2Id)
    {
        int port = GetNextAvailablePort();
        if (port == -1)
        {
            Console.WriteLine("[Matchmaking] No available ports for friendly match.");
            return null;
        }

        var match = await matchService.CreateMatchAsync(
            player1Id: player1Id,
            player2Id: player2Id,
            serverIp: _serverConfig.ServerIp,
            port: port,
            mode: "friendly",
            mapId: "MD_PVP_001"
        );

        int processId = SpawnServer(match.MatchId, port, "friendly", chapterId: 0, sceneId: 0);

        if (processId <= 0)
        {
            Console.WriteLine($"[Matchmaking] Failed to spawn server for friendly match {match.MatchId}");
            await matchService.DeleteAsync(match.MatchId);
            ReleasePort(port);
            return null;
        }

        await matchService.UpdateProcessIdAsync(match.MatchId, processId);

        Console.WriteLine($"[Matchmaking] Friendly match {match.MatchId} created " +
                          $"{player1Id} vs {player2Id} on port {port} PID={processId}");
        return match;
    }

    // -------------------------------------------------------
    // Shared Spawn Logic
    // -------------------------------------------------------

    private int SpawnServer(string matchId, int port, string mode, int chapterId, int sceneId)
    {
        try
        {
            string logPath = Path.Combine(_serverConfig.LocalPath, $"{matchId}.log");
            Directory.CreateDirectory(_serverConfig.LocalPath);

            // Server reads -chapterId and -sceneId to know which scene to load:
            // C{chapterId}_S{sceneId}_Server  (story mode only — pvp ignores these)
            var args = $"-server -matchId {matchId} -port {port} -mode {mode} " +
                       $"-chapterId {chapterId} -sceneId {sceneId} " +
                       $"-batchmode -nographics -logFile \"{logPath}\"";

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _serverConfig.UnityServerExePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = System.Diagnostics.Process.Start(startInfo);
            return process?.Id ?? -1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Matchmaking] Failed to spawn server: {ex.Message}");
            return -1;
        }
    }

    // Fallback map selection per scene when ChapterConfig.Scene.MapId is omitted.
    private static string GetMapIdForScene(int chapterId, int sceneId) => (chapterId, sceneId) switch
    {
        (0, 1) => "MD_PVE_C0S1",
        (0, 2) => "MD_PVE_C0S2",
        (0, 3) => "MD_PVE_C0S3",
        (1, 1) => "MD_PVE_C1S1",
        (1, 2) => "MD_PVE_C1S2",
        (1, 3) => "MD_PVE_C1S3",
        (2, 1) => "MD_PVE_C2S1",
        (2, 2) => "MD_PVE_C2S2",
        (3, 1) => "MD_PVE_C3S1",
        (3, 2) => "MD_PVE_C3S2",
        _ => "MD_PVP_001"
    };
}
