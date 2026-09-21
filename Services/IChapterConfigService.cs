using GameInventoryApi.Models;

namespace GameInventoryApi.Services;

public interface IChapterConfigService
{
    Task<List<ChapterConfig>> GetAllAsync();
    Task<ChapterConfig?> GetByChapterIdAsync(int chapterId);
    Task<ChapterConfig?> GetByMongoIdAsync(string id);
    Task<SceneConfig?> GetSceneAsync(int chapterId, int sceneId);
    Task<ChapterConfig> CreateAsync(ChapterConfig chapter);
    Task<bool> UpdateAsync(string id, ChapterConfig chapter);
    Task<bool> DeleteAsync(string id);
}