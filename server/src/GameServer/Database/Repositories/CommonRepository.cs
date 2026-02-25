using Dapper;
using GameServer.Database.Models;
using Npgsql;
using Serilog;

namespace GameServer.Database.Repositories;

/// <summary>
/// Common database repository
/// </summary>
public class CommonRepository
{
    private readonly string _connectionString;

    public CommonRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Get all characters for an account
    /// </summary>
    public async Task<List<CharacterSummary>> GetCharactersByAccountIdAsync(long accountId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT character_id as CharacterId, account_id as AccountId,
                       server_id as ServerId, character_name as CharacterName,
                       character_level as CharacterLevel, character_class as CharacterClass,
                       last_played_at as LastPlayedAt, created_at as CreatedAt,
                       is_deleted as IsDeleted, deleted_at as DeletedAt
                FROM character_summaries
                WHERE account_id = @AccountId AND is_deleted = FALSE
                ORDER BY last_played_at DESC";

            var characters = await connection.QueryAsync<CharacterSummary>(sql, new { AccountId = accountId });
            return characters.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get characters for account: {AccountId}", accountId);
            return new List<CharacterSummary>();
        }
    }

    /// <summary>
    /// Get character by ID
    /// </summary>
    public async Task<CharacterSummary?> GetCharacterByIdAsync(long characterId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT character_id as CharacterId, account_id as AccountId,
                       server_id as ServerId, character_name as CharacterName,
                       character_level as CharacterLevel, character_class as CharacterClass,
                       last_played_at as LastPlayedAt, created_at as CreatedAt,
                       is_deleted as IsDeleted, deleted_at as DeletedAt
                FROM character_summaries
                WHERE character_id = @CharacterId";

            return await connection.QuerySingleOrDefaultAsync<CharacterSummary>(sql, new { CharacterId = characterId });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get character: {CharacterId}", characterId);
            return null;
        }
    }

    /// <summary>
    /// Update character last played time and level
    /// </summary>
    public async Task<bool> UpdateCharacterSummaryAsync(long characterId, int level)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE character_summaries
                SET character_level = @Level, last_played_at = CURRENT_TIMESTAMP
                WHERE character_id = @CharacterId";

            var affected = await connection.ExecuteAsync(sql, new { CharacterId = characterId, Level = level });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update character summary: {CharacterId}", characterId);
            return false;
        }
    }

    /// <summary>
    /// Create new character
    /// </summary>
    public async Task<long?> CreateCharacterAsync(long accountId, int serverId, string characterName, string characterClass)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO character_summaries (account_id, server_id, character_name, character_class)
                VALUES (@AccountId, @ServerId, @CharacterName, @CharacterClass)
                RETURNING character_id";

            return await connection.QuerySingleAsync<long>(sql, new
            {
                AccountId = accountId,
                ServerId = serverId,
                CharacterName = characterName,
                CharacterClass = characterClass
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create character: {CharacterName}", characterName);
            return null;
        }
    }
}
