using Action = GameInventoryApi.Models.Action;

namespace GameInventoryApi.Services;

public interface IRewardEvaluationService
{
    Task EvaluateAsync(Action action);
}
