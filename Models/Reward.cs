using System.Text.Json.Serialization;

namespace GameInventoryApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RewardType { Unit, Weapon, Trinket, Gems }

public class Reward
{
    public RewardType Type { get; set; }
    public int DefinitionId { get; set; }  // Unit UId / Weapon WeaponId / Trinket TrinketId; 0 for Gems
    public int ClassId { get; set; }       // Unit only: which class to unlock; must be in UnitDefinition.ClassIds
    public int Amount { get; set; } = 1;   // Gem count when Type == Gems; 1 for items
}
