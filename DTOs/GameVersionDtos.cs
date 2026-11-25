namespace GameInventoryApi.DTOs;

public class GameVersionDto
{
    public string LatestVersion { get; set; } = "1.0.0";
    public string MinSupportedVersion { get; set; } = "1.0.0";
    public string DownloadUrl { get; set; } = string.Empty;
    public string PatchNotes { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class UpdateGameVersionDto
{
    public string LatestVersion { get; set; } = string.Empty;
    public string MinSupportedVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? PatchNotes { get; set; }
}
