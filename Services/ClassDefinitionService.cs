using GameInventoryApi.DTOs;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class ClassDefinitionService : IClassDefinitionService
{
    private readonly IMongoRepository<ClassDefinition> _repository;

    public ClassDefinitionService(IMongoRepository<ClassDefinition> repository) => _repository = repository;

    public Task<List<ClassDefinition>> GetAllAsync() => _repository.GetAllAsync();
    public Task<ClassDefinition?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task<ClassDefinition?> GetByClassIdAsync(int classId) => _repository.GetByFilterAsync(c => c.ClassId == classId);
    public Task CreateAsync(ClassDefinition classDef) => _repository.CreateAsync(classDef);
    public Task UpdateAsync(string id, ClassDefinition classDef) => _repository.UpdateAsync(id, classDef);
    public Task DeleteAsync(string id) => _repository.DeleteAsync(id);

    public async Task<List<ClassSkillResultDto>> GetMovementSkillsByClassIdsAsync(List<int> classIds)
    {
        var defs = await _repository.GetAllByFilterAsync(c => classIds.Contains(c.ClassId));
        return defs.Select(c => new ClassSkillResultDto(c.ClassId, c.MovementSkillId)).ToList();
    }

    public async Task<List<ClassSkillResultDto>> GetClassSkillsByClassIdsAsync(List<int> classIds)
    {
        var defs = await _repository.GetAllByFilterAsync(c => classIds.Contains(c.ClassId));
        return defs.Select(c => new ClassSkillResultDto(c.ClassId, c.ClassSkillId)).ToList();
    }
}
