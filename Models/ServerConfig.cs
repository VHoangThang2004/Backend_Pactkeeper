namespace GameInventoryApi.Models;

public class ServerConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public IEnumerable<int> GamePorts { get; set; } = new List<int> { 7777, 7778, 7779, 7780 };
    public string BackendPublicUrl { get; set; } = string.Empty;
    public string UnityServerExePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}