using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameInventoryApi.Models;
using Microsoft.Extensions.Options;

namespace GameInventoryApi.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly LlmSettings _settings;

    public LlmService(HttpClient httpClient, IOptions<LlmSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<(List<BalanceRecommendationItem> Recommendations, string RawResponse)> GetBalancingSuggestionsAsync(
        string patchVersion,
        int totalMatches,
        List<BalanceAnomalyItem> anomalies)
    {
        if (anomalies == null || anomalies.Count == 0)
        {
            return (new List<BalanceRecommendationItem>(), "No balance anomalies detected (all items within average winrate thresholds).");
        }

        string prompt = BuildBalancePrompt(patchVersion, totalMatches, anomalies);

        // If no API key is configured or offline development, use smart rule-based fallback
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            Console.WriteLine("[LlmService] No API Key configured. Generating intelligent fallback suggestions.");
            var fallbackRecommendations = GenerateFallbackSuggestions(anomalies);
            string fallbackJson = JsonSerializer.Serialize(fallbackRecommendations, new JsonSerializerOptions { WriteIndented = true });
            return (fallbackRecommendations, fallbackJson);
        }

        try
        {
            if (string.Equals(_settings.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return await CallOpenAiAsync(prompt);
            }
            else
            {
                return await CallGeminiAsync(prompt);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LlmService] Error calling LLM API: {ex.Message}. Using fallback suggestions.");
            var fallback = GenerateFallbackSuggestions(anomalies);
            return (fallback, $"Error calling LLM ({ex.Message}). Fallback applied.");
        }
    }

    private string BuildBalancePrompt(string patchVersion, int totalMatches, List<BalanceAnomalyItem> anomalies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert game balance designer for a turn-based Strategy RPG (SRPG).");
        sb.AppendLine("Combat is governed by an authoritative timeline of Instants & Steps, Unit speed tiers (100-140), max HP (1-5), and Skill Points (SP).");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT CLASSIFICATION CRITERIA (changeType):");
        sb.AppendLine("- DB_ONLY: The change can be made strictly by updating database fields in MongoDB (e.g. UnitDefinitions MaxHP, Speed, MaxSkillPoint, DamageMultiplier, DamageReduction, or Weapon/Trinket StatModifiers). No code changes or client/server rebuild required.");
        sb.AppendLine("- REBUILD_REQUIRED: The change alters skill behavior, targeting geometry/range, instant/step mechanics, passive triggers, or client/server code. This requires recompiling the Unity Dedicated Server and Game Client.");
        sb.AppendLine();
        sb.AppendLine($"DATA FOR PATCH {patchVersion} (Total Matches Analyzed: {totalMatches}):");
        foreach (var a in anomalies)
        {
            sb.AppendLine($"- [{a.Category}] ID={a.TargetId} Name='{a.TargetName}' | PickCount={a.PickCount} | PickRate={(a.PickRate * 100):F1}% | WinRate={(a.WinRate * 100):F1}% | SurvivalRate={(a.SurvivalRate * 100):F1}% | AvgDmg={a.AvgDamage:F1} | Status={a.AnomalyType}");
        }
        sb.AppendLine();
        sb.AppendLine("TASK:");
        sb.AppendLine("Analyze each anomalous item and provide concrete balancing recommendations.");
        sb.AppendLine("Respond ONLY with a JSON array conforming to this schema:");
        sb.AppendLine("[");
        sb.AppendLine("  {");
        sb.AppendLine("    \"changeType\": \"DB_ONLY\" or \"REBUILD_REQUIRED\",");
        sb.AppendLine("    \"category\": \"Unit\" | \"Weapon\" | \"Trinket\" | \"Skill\",");
        sb.AppendLine("    \"targetId\": <int>,");
        sb.AppendLine("    \"targetName\": \"<string>\",");
        sb.AppendLine("    \"currentWinRate\": <double between 0 and 1>,");
        sb.AppendLine("    \"issueDescription\": \"<concise problem summary>\",");
        sb.AppendLine("    \"recommendedChange\": \"<exact adjustment proposed>\",");
        sb.AppendLine("    \"reasoning\": \"<why this adjustment balances the item>\"");
        sb.AppendLine("  }");
        sb.AppendLine("]");

        return sb.ToString();
    }

    private async Task<(List<BalanceRecommendationItem>, string)> CallGeminiAsync(string prompt)
    {
        string model = string.IsNullOrEmpty(_settings.Model) ? "gemini-1.5-flash" : _settings.Model;
        string url = string.IsNullOrEmpty(_settings.EndpointUrl)
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_settings.ApiKey}"
            : $"{_settings.EndpointUrl.TrimEnd('/')}/{model}:generateContent?key={_settings.ApiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        var response = await _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        string rawResponse = await response.Content.ReadAsStringAsync();

        string jsonContent = string.Empty;
        using (var doc = JsonDocument.Parse(rawResponse))
        {
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElem))
            {
                jsonContent = textElem.GetString() ?? string.Empty;
            }
        }

        var list = ParseRecommendations(jsonContent);
        return (list, jsonContent);
    }

    private async Task<(List<BalanceRecommendationItem>, string)> CallOpenAiAsync(string prompt)
    {
        string url = string.IsNullOrEmpty(_settings.EndpointUrl)
            ? "https://api.openai.com/v1/chat/completions"
            : _settings.EndpointUrl;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var requestBody = new
        {
            model = string.IsNullOrEmpty(_settings.Model) ? "gpt-4o-mini" : _settings.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            response_format = new { type = "json_object" }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string rawResponse = await response.Content.ReadAsStringAsync();
        string jsonContent = string.Empty;

        using (var doc = JsonDocument.Parse(rawResponse))
        {
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentElem))
            {
                jsonContent = contentElem.GetString() ?? string.Empty;
            }
        }

        var list = ParseRecommendations(jsonContent);
        return (list, jsonContent);
    }

    private List<BalanceRecommendationItem> ParseRecommendations(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return [];

        try
        {
            // Strip markdown block fences if present e.g. ```json ... ```
            string cleaned = rawJson.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(7);
            }
            else if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (cleaned.StartsWith("["))
            {
                return JsonSerializer.Deserialize<List<BalanceRecommendationItem>>(cleaned, options) ?? [];
            }
            else if (cleaned.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(cleaned);
                if (doc.RootElement.TryGetProperty("recommendations", out var recs))
                {
                    return JsonSerializer.Deserialize<List<BalanceRecommendationItem>>(recs.GetRawText(), options) ?? [];
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LlmService] Failed to deserialize LLM recommendations: {ex.Message}");
        }

        return [];
    }

    private List<BalanceRecommendationItem> GenerateFallbackSuggestions(List<BalanceAnomalyItem> anomalies)
    {
        var result = new List<BalanceRecommendationItem>();
        foreach (var a in anomalies)
        {
            bool isOverperforming = a.WinRate >= 0.60;
            string changeType = "DB_ONLY";
            string recChange;
            string rationale;
            string issue;

            if (isOverperforming)
            {
                issue = $"High win rate ({a.WinRate * 100:F1}%) and high presence ({a.PickRate * 100:F1}%) indicating dominant combat impact.";
                if (a.Category == "Unit")
                {
                    changeType = "DB_ONLY";
                    recChange = $"Reduce base Speed by 5-10 or decrease DamageMultiplier in UnitDefinitions for Unit {a.TargetId}.";
                    rationale = "Reducing Speed delays turn order timeline placement without requiring client code changes.";
                }
                else if (a.Category == "Weapon" || a.Category == "Trinket")
                {
                    changeType = "DB_ONLY";
                    recChange = $"Tune down StatModifiers (MaxHP -1 or Speed -5) in {a.Category}Definitions for item {a.TargetId}.";
                    rationale = "Equipment stat modifiers directly load from MongoDB and can be balanced instantly.";
                }
                else // Skill
                {
                    changeType = "REBUILD_REQUIRED";
                    recChange = $"Increase SP cost by 1 or narrow area-of-effect targeting for Skill {a.TargetId}.";
                    rationale = "Skill action mechanics and targeting geometry require updating game client and server logic.";
                }
            }
            else
            {
                issue = $"Low win rate ({a.WinRate * 100:F1}%) suggesting the item feels underpowered or ineffective in the current meta.";
                if (a.Category == "Unit")
                {
                    changeType = "DB_ONLY";
                    recChange = $"Increase MaxHP by +1 or increase Speed by +5 in UnitDefinitions for Unit {a.TargetId}.";
                    rationale = "Increasing survivability or speed grants the unit more actionable steps during combat.";
                }
                else if (a.Category == "Weapon" || a.Category == "Trinket")
                {
                    changeType = "DB_ONLY";
                    recChange = $"Add +1 MaxSkillPoint or +5 Speed modifier in {a.Category}Definitions for item {a.TargetId}.";
                    rationale = "Equipment buff in MongoDB provides immediate player incentive.";
                }
                else
                {
                    changeType = "REBUILD_REQUIRED";
                    recChange = $"Reduce SP cost or enhance secondary status effect duration for Skill {a.TargetId}.";
                    rationale = "Altering skill resource efficiency or status effect logic requires re-baking game rules.";
                }
            }

            result.Add(new BalanceRecommendationItem
            {
                ChangeType = changeType,
                Category = a.Category,
                TargetId = a.TargetId,
                TargetName = a.TargetName,
                CurrentWinRate = Math.Round(a.WinRate, 4),
                IssueDescription = issue,
                RecommendedChange = recChange,
                Reasoning = rationale
            });
        }

        return result;
    }
}
