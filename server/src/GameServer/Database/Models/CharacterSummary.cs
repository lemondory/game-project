namespace GameServer.Database.Models;

/// <summary>
/// Common DB - character_summaries table
/// </summary>
public class CharacterSummary
{
    public long CharacterId { get; set; }
    public long AccountId { get; set; }
    public int ServerId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public int CharacterLevel { get; set; }
    public string? CharacterClass { get; set; }
    public DateTime LastPlayedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
