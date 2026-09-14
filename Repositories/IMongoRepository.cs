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
}