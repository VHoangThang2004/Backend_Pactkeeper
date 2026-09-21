using GameInventoryApi.Models;
using GameInventoryApi.Repositories;

namespace GameInventoryApi.Services;

public class ChapterConfigService : IChapterConfigService
{
    private readonly IMongoRepository<ChapterConfig> _repository;

    public ChapterConfigService(IMongoRepository<ChapterConfig> repository)
    {
        _repository = repository;
    }

    public Task<List<ChapterConfig>> GetAllAsync()
        => _repository.GetAllAsync();

    public Task<ChapterConfig?> GetByChapterIdAsync(int chapterId)
        => _repository.GetByFilterAsync(c => c.ChapterId == chapterId);

    public Task<ChapterConfig?> GetByMongoIdAsync(string id)
        => _repository.GetByIdAsync(id);

    public async Task<SceneConfig?> GetSceneAsync(int chapterId, int sceneId)
    {
        var chapter = await GetByChapterIdAsync(chapterId);
        return chapter?.Scenes.FirstOrDefault(s => s.SceneId == sceneId);
    }

    public async Task<ChapterConfig> CreateAsync(ChapterConfig chapter)
    {
        await _repository.CreateAsync(chapter);
        return chapter;
    }

    public async Task<bool> UpdateAsync(string id, ChapterConfig chapter)
    {
        var existing = await _repository.GetByIdAsync(id);
        if (existing == null) return false;
        await _repository.UpdateAsync(id, chapter);
        return true;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var existing = await _repository.GetByIdAsync(id);
        if (existing == null) return false;
        await _repository.DeleteAsync(id);
        return true;
    }
}