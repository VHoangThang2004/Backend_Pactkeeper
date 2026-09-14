using System.Numerics;

namespace GameInventoryApi.RealTime;

public class PlayerState
{
    public string PlayerId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
    public float RotationY { get; set; }
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}