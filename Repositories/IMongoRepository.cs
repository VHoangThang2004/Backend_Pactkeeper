using MongoDB.Driver;
using System.Linq.Expressions;

namespace GameInventoryApi.Repositories;

public interface IMongoRepository<T> where T : class
{
    Task<List<T>> GetAllAsync();
    Task<T?> GetByIdAsync(string id);
    Task<T?> GetByFilterAsync(Expression<Func<T, bool>> filter);
    Task CreateAsync(T entity);
    Task UpdateAsync(string id, T entity);
    Task DeleteAsync(string id);
    // Atomic: applies `update` to the first match of `filter`; returns it as it was before, or null if none matched
    Task<T?> FindOneAndUpdateAsync(Expression<Func<T, bool>> filter, UpdateDefinition<T> update);
    Task<List<T>> GetAllByFilterAsync(Expression<Func<T, bool>> filter);
    Task<List<T>> GetTopAsync(Expression<Func<T, bool>> filter, Expression<Func<T, object>> sortByDescending, int limit);
}