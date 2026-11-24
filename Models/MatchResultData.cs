namespace GameInventoryApi.Models;

public class AfterMatchPlayerData
{
    public string PlayerId { get; set; } = string.Empty;
    public int UnitsAlive { get; set; }
    public int TotalUnits { get; set; }
    public int UnitsDefeated { get; set; }
    public List<LoadoutUnitData> Loadout { get; set; } = [];
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int HealingDone { get; set; }
    public int Timeouts { get; set; }
    public List<UnitStatsData> Units { get; set; } = [];
}

public class UnitStatsData
{
    public int UId { get; set; }
    public bool Alive { get; set; }
    public int DamageDealt { get; set; }
    public int DamageTaken { get; set; }
    public int HealingDone { get; set; }
    public int Kills { get; set; }
    public int DiedAtInstant { get; set; } = -1;
    public int KilledByUId { get; set; } = -1;
    public List<SkillUseData> SkillUses { get; set; } = [];
}

public class SkillUseData
{
    public int SkillId { get; set; }
    public int Count { get; set; }
}

public class LoadoutUnitData
{
    public int UId { get; set; }
    public int PassiveSkillId { get; set; }
    public int MovementSkillId { get; set; }
    public int WeaponSkillId { get; set; }
    public int ClassSkillId { get; set; }
    public int TrinketSkillId { get; set; }
}

public class MatchResultData
{
    public string WinnerId { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int TotalInstants { get; set; }
    public bool IsForfeit { get; set; } = false;
    public string? ForfeitPlayerId { get; set; } = null;
    public List<AfterMatchPlayerData> AfterMatchTeamData { get; set; } = [];
}