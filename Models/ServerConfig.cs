namespace GameInventoryApi.Models;

public class ServerConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public int BasePort { get; set; } = 7777;
    public string BackendPublicUrl { get; set; } = string.Empty;
    public string UnityServerExePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}