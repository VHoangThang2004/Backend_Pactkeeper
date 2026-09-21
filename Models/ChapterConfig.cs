using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameInventoryApi.Models;

public enum SceneType
{
    Cutscene,   // Client completes — watching only
    Dialogue,   // Client completes — conversation/tooltip
    Battle      // Server completes — must win to complete
}

public class SceneConfig
{
    public int SceneId { get; set; }
    public SceneType Type { get; set; }

    // If true, client calls /next immediately after /complete without waiting for player input
    public bool AutoNext { get; set; } = false;

    public string Description { get; set; } = string.Empty;
}

public class ChapterConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public int ChapterId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MapId { get; set; } = "MD_PVP_001";
    public List<SceneConfig> Scenes { get; set; } = new();
}