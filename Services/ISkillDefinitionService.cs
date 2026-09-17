using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface ISkillDefinitionService
{
    Task<List<SkillDefinition>> GetAllAsync();
    Task<SkillDefinition?> GetByIdAsync(string id);
    Task<SkillDefinition?> GetBySkillIdAsync(int skillId);
    Task CreateAsync(SkillDefinition skill);
    Task UpdateAsync(string id, SkillDefinition skill);
    Task DeleteAsync(string id);
}
