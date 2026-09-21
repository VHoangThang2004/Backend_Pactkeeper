using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public class StoryProgress
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;

    // One document per chapter per player
    public int ChapterId { get; set; }
    public int SceneId { get; set; } = 0;

    // False = scene is active, client should run it now
    // True = scene done, waiting for /next to be called
    public bool IsCompleted { get; set; } = false;

    // True once ALL scenes in this chapter are done
    public bool IsChapterCompleted { get; set; } = false;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; } = null;
}