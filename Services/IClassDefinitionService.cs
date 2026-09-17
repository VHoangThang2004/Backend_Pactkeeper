using GameInventoryApi.DTOs;
using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IClassDefinitionService
{
    Task<List<ClassDefinition>> GetAllAsync();
    Task<ClassDefinition?> GetByIdAsync(string id);
    Task<ClassDefinition?> GetByClassIdAsync(int classId);
    Task CreateAsync(ClassDefinition classDef);
    Task UpdateAsync(string id, ClassDefinition classDef);
    Task DeleteAsync(string id);

    Task<List<ClassSkillResultDto>> GetMovementSkillsByClassIdsAsync(List<int> classIds);
    Task<List<ClassSkillResultDto>> GetClassSkillsByClassIdsAsync(List<int> classIds);
}
