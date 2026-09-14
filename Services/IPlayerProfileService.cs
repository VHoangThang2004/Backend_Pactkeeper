using System.Linq.Expressions;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IPlayerProfileService
{
    Task<PlayerProfile?> GetByFilterAsync(Expression<Func<PlayerProfile, bool>> filter);
    Task UpdateAsync(string id, PlayerProfile profile);
}