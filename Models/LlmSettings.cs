namespace GameInventoryApi.Models;

public class LlmSettings
{
    public string Provider { get; set; } = "Gemini"; // "Gemini" or "OpenAI"
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-1.5-flash";
    public string EndpointUrl { get; set; } = string.Empty;
    public double WinRateHighThreshold { get; set; } = 0.60;
    public double WinRateLowThreshold { get; set; } = 0.40;
    public int MinPickCount { get; set; } = 5;
}
