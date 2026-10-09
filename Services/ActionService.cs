using GameInventoryApi.Repositories;
using Action = GameInventoryApi.Models.Action;

namespace GameInventoryApi.Services;

public class ActionService(
    IMongoRepository<Action> repository,
    IRewardEvaluationService rewardEvaluationService) : IActionService
{
    public async Task<Action> LogActionAsync(string playerId, string actionType, string referenceId, Dictionary<string, object> facts)
    {
        var action = new Action
        {
            PlayerId = playerId,
            ActionType = actionType,
            ReferenceId = referenceId,
            Facts = facts,
            Timestamp = DateTime.UtcNow,
        };
        await repository.CreateAsync(action);

        await rewardEvaluationService.EvaluateAsync(action);

        return action;
    }
}
