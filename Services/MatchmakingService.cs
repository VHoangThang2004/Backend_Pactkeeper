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
                if (_activePorts.Add(port))
                    return port;
            return -1;
        }
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

    // Only "is that PID still running" — no name check: on Linux ProcessName is truncated (15 chars),
    // and a false "alive" just leaves the match to the max-age rule.
    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch { return false; }
    }

    // -------------------------------------------------------
    // Reaper — ends matches whose server crashed, never reported, or ran far past the 60-min match cap
    // -------------------------------------------------------

    private static readonly TimeSpan SpawnGrace = TimeSpan.FromSeconds(30); // ProcessId is written just after creation
    private static readonly TimeSpan MaxMatchAge = TimeSpan.FromMinutes(75);

    private async Task ReapDeadMatches()
    {
        using var scope = _scopeFactory.CreateScope();
        var matchService = scope.ServiceProvider.GetRequiredService<IMatchSessionService>();

        foreach (var match in await matchService.GetAllAsync())
        {
            var age = DateTime.UtcNow - match.CreatedAt;
            if (age > MaxMatchAge)
                await matchService.TerminateMatchAsync(match, $"older than {MaxMatchAge.TotalMinutes} min");
            // ProcessId <= 0 = server not spawned by us (e.g. dev server in the Editor) — only the age rule applies
            else if (age > SpawnGrace && match.ProcessId > 0 && !IsProcessAlive(match.ProcessId))
                await matchService.TerminateMatchAsync(match, "server process is gone");
            else
                MarkPortInUse(match.ServerPort); // after a backend restart, live matches still own their ports
        }
    }

    // Kills a headless match server process if it is still running.
    // Verifies the process name matches the Unity server exe so a recycled PID
    // (e.g. after a backend restart) never kills an unrelated process.
    // Waits for exit so the UDP port is really free before it is reused.
    public bool TryKillServerProcess(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (process.HasExited) return false;

            var expectedName = Path.GetFileNameWithoutExtension(_serverConfig.UnityServerExePath);
            if (!string.IsNullOrEmpty(expectedName) &&
                !string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[Matchmaking] PID {processId} is '{process.ProcessName}', " +
                                  $"not '{expectedName}' — skip kill.");
                return false;
            }

            process.Kill();
            process.WaitForExit(5000);
            Console.WriteLine($"[Matchmaking] Killed server process {processId}");
            return true;
        }
        catch (ArgumentException)
        {
            // Process no longer running
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Matchmaking] Failed to kill process {processId}: {ex.Message}");
            return false;
        }
    }

    // -------------------------------------------------------
    // PvP Background Loop
    // -------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Matchmaking] Background service started.");
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
        if (processId > 0)
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
                if (processId > 0)
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
            mapId: mapId
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
