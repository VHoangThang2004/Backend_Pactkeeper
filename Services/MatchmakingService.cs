using GameInventoryApi.Models;
using Microsoft.Extensions.Options;

namespace GameInventoryApi.Services;

public class MatchmakingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServerConfig _serverConfig;

    private HashSet<int> _activePorts = new HashSet<int>();

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

    public MatchmakingService(IServiceScopeFactory scopeFactory, IOptions<ServerConfig> serverConfig)
    {
        _scopeFactory = scopeFactory;
        _serverConfig = serverConfig.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Matchmaking] Background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryMatchPlayers();
            await Task.Delay(3000, stoppingToken); // check every 3 seconds
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

        Console.WriteLine($"[Matchmaking] Pairing {player1.PlayerId} vs {player2.PlayerId}");

        // Create match
        int port = GetNextAvailablePort();
        if (port == -1)
        {
            Console.WriteLine("[Matchmaking] No available ports — cannot start match.");
            return;
        }
        var match = await matchService.CreateMatchAsync(
            player1.PlayerId, player2.PlayerId,
            _serverConfig.ServerIp, port, "pvp", "MD_PVP_001");

        // Spawn Unity server
        int processId = SpawnUnityServer(match.MatchId, port);
        if (processId > 0)
            await matchService.UpdateProcessIdAsync(match.MatchId, processId);

        // Update both queue entries
        await queueService.UpdateMatchedAsync(player1.PlayerId, match.MatchId, _serverConfig.ServerIp, port);
        await queueService.UpdateMatchedAsync(player2.PlayerId, match.MatchId, _serverConfig.ServerIp, port);

        Console.WriteLine($"[Matchmaking] Match {match.MatchId} created, server PID={processId}");
    }

    private int SpawnUnityServer(string matchId, int port)
    {
        try
        {
            string logPath = Path.Combine(_serverConfig.LocalPath, $"{matchId}.log");
            Directory.CreateDirectory(_serverConfig.LocalPath);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _serverConfig.UnityServerExePath,
                Arguments = $"-server -matchId {matchId} -port {port} -batchmode -nographics -logFile \"{logPath}\"",
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
}