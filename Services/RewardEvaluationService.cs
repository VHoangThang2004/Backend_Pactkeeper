using System.Globalization;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using Action = GameInventoryApi.Models.Action;

namespace GameInventoryApi.Services;

public class RewardEvaluationService(
    IMongoRepository<RewardRule> ruleRepository,
    IMongoRepository<RewardProgress> progressRepository,
    INotificationService notificationService) : IRewardEvaluationService
{
    public async Task EvaluateAsync(Action action)
    {
        var rules = await ruleRepository.GetAllByFilterAsync(r => r.ActionType == action.ActionType && r.IsActive);

        foreach (var rule in rules)
        {
            if (!rule.Conditions.All(c => EvaluateCondition(c, action.Facts)))
                continue;

            var progress = await progressRepository.GetByFilterAsync(
                p => p.PlayerId == action.PlayerId && p.RuleId == rule.Id);

            if (progress == null)
            {
                progress = new RewardProgress
                {
                    PlayerId = action.PlayerId,
                    RuleId = rule.Id,
                    CurrentCount = 0,
                    IsCompleted = false,
                };
                await progressRepository.CreateAsync(progress);
            }

            if (progress.IsCompleted) continue;

            int delta = 1;
            if (rule.AccumulateField != null && action.Facts.TryGetValue(rule.AccumulateField, out var raw))
                delta = Convert.ToInt32(raw);

            progress.CurrentCount += delta;

            if (progress.CurrentCount >= rule.TargetCount)
            {
                progress.IsCompleted = true;
                progress.CompletedAt = DateTime.UtcNow;

                // Reward is granted on claim (see RewardProgressController), not here.
                await notificationService.CreateNotificationAsync(
                    action.PlayerId,
                    "success",
                    "Objective Complete",
                    $"You've completed a {rule.ActionType} objective. Claim your reward!");
            }

            await progressRepository.UpdateAsync(progress.Id, progress);
        }
    }

    // Compares a Fact value (int/bool/string, as it round-trips from Mongo) against a RewardCondition.
    private static bool EvaluateCondition(RewardCondition condition, Dictionary<string, object> facts)
    {
        if (!facts.TryGetValue(condition.Field, out var actual) || actual == null)
            return false;

        if (actual is bool actualBool)
        {
            if (!bool.TryParse(condition.Value, out var expectedBool)) return false;
            return condition.Op switch
            {
                "==" => actualBool == expectedBool,
                "!=" => actualBool != expectedBool,
                _ => false,
            };
        }

        if (actual is string actualString)
        {
            return condition.Op switch
            {
                "==" => actualString == condition.Value,
                "!=" => actualString != condition.Value,
                _ => false,
            };
        }

        // Numeric (int/long/double as stored in Mongo)
        if (!double.TryParse(condition.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedNum))
            return false;
        double actualNum = Convert.ToDouble(actual, CultureInfo.InvariantCulture);

        return condition.Op switch
        {
            "==" => actualNum == expectedNum,
            "!=" => actualNum != expectedNum,
            ">" => actualNum > expectedNum,
            ">=" => actualNum >= expectedNum,
            "<" => actualNum < expectedNum,
            "<=" => actualNum <= expectedNum,
            _ => false,
        };
    }
}
