namespace GameServer.Database.Models;

/// <summary>
/// Game DB - players table
/// </summary>
public class Player
{
    public long PlayerId { get; set; }
    public long CharacterId { get; set; }
    public long AccountId { get; set; }
    public string CharacterName { get; set; } = string.Empty;

    // Stats
    public int Level { get; set; }
    public long Experience { get; set; }
    public string CharacterClass { get; set; } = "Warrior";

    // Position
    public string ZoneType { get; set; } = "Town";
    public int ZoneId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    // Combat
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int AttackPower { get; set; }
    public int Defense { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastLogoutAt { get; set; }
    public long TotalPlayTimeSeconds { get; set; }
}
