using Action = GameInventoryApi.Models.Action;

namespace GameInventoryApi.Services;

public interface IActionService
{
    Task<Action> LogActionAsync(string playerId, string actionType, string referenceId, Dictionary<string, object> facts);
}
