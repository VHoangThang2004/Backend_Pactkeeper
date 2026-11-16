namespace GameInventoryApi.Models;

public class ServerConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public IEnumerable<int> GamePorts { get; set; } = new List<int> { 7777, 7778, 7779, 7780 };
    public string BackendPublicUrl { get; set; } = string.Empty;
    public string UnityServerExePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    // Only the backend that hosts the game servers (the VPS) may run matchmaking + the reaper. Members' local
    // backends share the DB but must never pair queued players or touch match records → default off.
    public bool RunMatchmaking { get; set; } = false;
}