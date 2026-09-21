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

    private int GetNextAvailablePort()
    {
        foreach (var port in _serverConfig.GamePorts)
        {
            if (!_activePorts.Contains(port))
            {
                _activePorts.Add(port);
                return port;
            }
        }
        return -1;
    }

    public void ReleasePort(int port)
    {
        _activePorts.Remove(port);
        Console.WriteLine($"[Matchmaking] Released port {port}");
    }

    // -------------------------------------------------------
    // PvP Background Loop
    // -------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Matchmaking] Background service started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            await TryMatchPlayers();
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
            Console.WriteLine($"[Matchmaking] Queue is not enough, cannot match...");
            return;
        }

        var player1 = waiting[0];
        var player2 = waiting[1];

        var p1Ongoing = await matchService.GetActiveMatchByPlayerIdAsync(player1.PlayerId);
        var p2Ongoing = await matchService.GetActiveMatchByPlayerIdAsync(player2.PlayerId);
        if (p1Ongoing != null || p2Ongoing != null)
        {
            Console.WriteLine($"[Matchmaking] Skipping pair — player already in an ongoing match.");
            return;
        }

        Console.WriteLine($"[Matchmaking] Pairing {player1.PlayerId} vs {player2.PlayerId}");

        int port = GetNextAvailablePort();
        if (port == -1)
        {
            Console.WriteLine("[Matchmaking] No available ports — cannot start match.");
            return;
        }

        var match = await matchService.CreateMatchAsync(
            player1.PlayerId, player2.PlayerId,
            _serverConfig.ServerIp, port, "pvp", "MD_PVP_001");

        // PvP has no chapter/scene — pass 0/0, server ignores them in pvp mode
        int processId = SpawnServer(match.MatchId, port, "pvp", chapterId: 0, sceneId: 0);
        if (processId > 0)
            await matchService.UpdateProcessIdAsync(match.MatchId, processId);

        await queueService.UpdateMatchedAsync(player1.PlayerId, match.MatchId, _serverConfig.ServerIp, port);
        await queueService.UpdateMatchedAsync(player2.PlayerId, match.MatchId, _serverConfig.ServerIp, port);

        Console.WriteLine($"[Matchmaking] Match {match.MatchId} created, server PID={processId}");
    }

    // -------------------------------------------------------
    // Story Match — called on-demand from StoryController
    // -------------------------------------------------------

    public async Task<MatchSession?> CreateStoryMatchAsync(
        IMatchSessionService matchService,
        string playerId,
        int chapterId,
        int sceneId)
    {
        int port = GetNextAvailablePort();
        if (port == -1)
        {
            Console.WriteLine("[Matchmaking] No available ports for story match.");
            return null;
        }

        var mapId = GetMapIdForScene(chapterId, sceneId);

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

    // Map selection per scene — currently all chapters/scenes use the same map.
    // Extend this switch as new chapters introduce different maps.
    private static string GetMapIdForScene(int chapterId, int sceneId) => (chapterId, sceneId) switch
    {
        (0, 1) => "MD_PVE_000",   // Chapter 0 Scene 1 — combat tutorial 
        _ => "MD_PVP_001"
    };
}