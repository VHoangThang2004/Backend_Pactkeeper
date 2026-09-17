using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class SkillDefinitionService : ISkillDefinitionService
{
    private readonly IMongoRepository<SkillDefinition> _repository;

    public SkillDefinitionService(IMongoRepository<SkillDefinition> repository) => _repository = repository;

    public Task<List<SkillDefinition>> GetAllAsync() => _repository.GetAllAsync();
    public Task<SkillDefinition?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task<SkillDefinition?> GetBySkillIdAsync(int skillId) => _repository.GetByFilterAsync(s => s.SkillId == skillId);
    public Task CreateAsync(SkillDefinition skill) => _repository.CreateAsync(skill);
    public Task UpdateAsync(string id, SkillDefinition skill) => _repository.UpdateAsync(id, skill);
    public Task DeleteAsync(string id) => _repository.DeleteAsync(id);
}
