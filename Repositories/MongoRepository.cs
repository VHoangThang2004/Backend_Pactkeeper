using MongoDB.Driver;
using System.Linq.Expressions;

namespace GameInventoryApi.Repositories;

public class MongoRepository<T> : IMongoRepository<T> where T : class
{
    protected readonly IMongoCollection<T> _collection;

    public MongoRepository(IMongoDatabase database, string collectionName)
    {
        _collection = database.GetCollection<T>(collectionName);
    }

    public async Task<List<T>> GetAllAsync() => await _collection.Find(_ => true).ToListAsync();
    public async Task<T?> GetByIdAsync(string id) => await _collection.Find(Builders<T>.Filter.Eq("_id", id)).FirstOrDefaultAsync();
    public async Task<T?> GetByFilterAsync(Expression<Func<T, bool>> filter) => await _collection.Find(filter).FirstOrDefaultAsync();
    public async Task CreateAsync(T entity) => await _collection.InsertOneAsync(entity);

    public async Task UpdateAsync(string id, T entity) =>
        await _collection.ReplaceOneAsync(
            Builders<T>.Filter.Eq("_id", MongoDB.Bson.ObjectId.Parse(id)),
            entity);
    public async Task DeleteAsync(string id) =>
        await _collection.DeleteOneAsync(
            Builders<T>.Filter.Eq("_id", MongoDB.Bson.ObjectId.Parse(id)));

    public async Task<List<T>> GetAllByFilterAsync(Expression<Func<T, bool>> filter) =>
        await _collection.Find(filter).ToListAsync();
}