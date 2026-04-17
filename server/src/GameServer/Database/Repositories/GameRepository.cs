using Dapper;
using GameServer.Database.Models;
using Npgsql;
using Serilog;

namespace GameServer.Database.Repositories;

/// <summary>
/// Game database repository
/// </summary>
public class GameRepository
{
    private readonly string _connectionString;

    public GameRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Get player by character ID
    /// </summary>
    public async Task<Player?> GetPlayerByCharacterIdAsync(long characterId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT player_id as PlayerId, character_id as CharacterId, account_id as AccountId,
                       character_name as CharacterName, level as Level, experience as Experience,
                       character_class as CharacterClass, zone_type as ZoneType, zone_id as ZoneId,
                       position_x as PositionX, position_y as PositionY, position_z as PositionZ,
                       max_hp as MaxHp, current_hp as CurrentHp, attack_power as AttackPower,
                       defense as Defense, created_at as CreatedAt, last_login_at as LastLoginAt,
                       last_logout_at as LastLogoutAt, total_play_time_seconds as TotalPlayTimeSeconds
                FROM players
                WHERE character_id = @CharacterId";

            return await connection.QuerySingleOrDefaultAsync<Player>(sql, new { CharacterId = characterId });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get player: {CharacterId}", characterId);
            return null;
        }
    }

    /// <summary>
    /// Create new player
    /// </summary>
    public async Task<long?> CreatePlayerAsync(long characterId, long accountId, string characterName, string characterClass)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO players (character_id, account_id, character_name, character_class)
                VALUES (@CharacterId, @AccountId, @CharacterName, @CharacterClass)
                RETURNING player_id";

            return await connection.QuerySingleAsync<long>(sql, new
            {
                CharacterId = characterId,
                AccountId = accountId,
                CharacterName = characterName,
                CharacterClass = characterClass
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create player: {CharacterName}", characterName);
            return null;
        }
    }

    /// <summary>
    /// Update player position and zone
    /// </summary>
    public async Task<bool> UpdatePlayerPositionAsync(long playerId, string zoneType, int zoneId, float x, float y, float z)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE players
                SET zone_type = @ZoneType, zone_id = @ZoneId,
                    position_x = @X, position_y = @Y, position_z = @Z
                WHERE player_id = @PlayerId";

            var affected = await connection.ExecuteAsync(sql, new
            {
                PlayerId = playerId,
                ZoneType = zoneType,
                ZoneId = zoneId,
                X = x,
                Y = y,
                Z = z
            });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update player position: {PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// Update player stats (HP, level, etc.)
    /// </summary>
    public async Task<bool> UpdatePlayerStatsAsync(long playerId, int level, long experience, int currentHp)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE players
                SET level = @Level, experience = @Experience, current_hp = @CurrentHp
                WHERE player_id = @PlayerId";

            var affected = await connection.ExecuteAsync(sql, new
            {
                PlayerId = playerId,
                Level = level,
                Experience = experience,
                CurrentHp = currentHp
            });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update player stats: {PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// Update player login time
    /// </summary>
    public async Task<bool> UpdatePlayerLoginAsync(long playerId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE players
                SET last_login_at = CURRENT_TIMESTAMP
                WHERE player_id = @PlayerId";

            var affected = await connection.ExecuteAsync(sql, new { PlayerId = playerId });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update player login: {PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// Update player logout time and total play time
    /// </summary>
    public async Task<bool> UpdatePlayerLogoutAsync(long playerId, long additionalPlayTimeSeconds)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE players
                SET last_logout_at = CURRENT_TIMESTAMP,
                    total_play_time_seconds = total_play_time_seconds + @AdditionalPlayTimeSeconds
                WHERE player_id = @PlayerId";

            var affected = await connection.ExecuteAsync(sql, new
            {
                PlayerId = playerId,
                AdditionalPlayTimeSeconds = additionalPlayTimeSeconds
            });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update player logout: {PlayerId}", playerId);
            return false;
        }
    }

    // ── 시간제 사냥터 쿼터 ───────────────────────────────────────────────────

    /// <summary>
    /// 플레이어의 필드 쿼터를 조회한다.
    /// 없으면 null 반환 → 호출자가 기본값 0으로 처리.
    /// 일간/주간 리셋 체크는 서버에서 처리한다.
    /// </summary>
    public async Task<PlayerFieldQuota?> GetFieldQuotaAsync(long playerId, int fieldId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT quota_id as QuotaId, player_id as PlayerId, field_id as FieldId,
                       daily_used_seconds as DailyUsedSeconds, weekly_used_seconds as WeeklyUsedSeconds,
                       last_daily_reset as LastDailyReset, last_weekly_reset as LastWeeklyReset,
                       last_entered_at as LastEnteredAt, last_saved_at as LastSavedAt
                FROM player_field_quota
                WHERE player_id = @PlayerId AND field_id = @FieldId";

            return await connection.QuerySingleOrDefaultAsync<PlayerFieldQuota>(sql,
                new { PlayerId = playerId, FieldId = fieldId });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetFieldQuota failed: playerId={PlayerId}, fieldId={FieldId}", playerId, fieldId);
            return null;
        }
    }

    /// <summary>
    /// 쿼터를 저장한다 (UPSERT). 리셋 날짜도 함께 저장.
    /// </summary>
    public async Task<bool> SaveFieldQuotaAsync(long playerId, int fieldId,
        int dailyUsedSeconds, int weeklyUsedSeconds,
        DateTime lastDailyReset, DateTime lastWeeklyReset)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO player_field_quota
                    (player_id, field_id, daily_used_seconds, weekly_used_seconds,
                     last_daily_reset, last_weekly_reset, last_saved_at)
                VALUES
                    (@PlayerId, @FieldId, @DailyUsedSeconds, @WeeklyUsedSeconds,
                     @LastDailyReset, @LastWeeklyReset, CURRENT_TIMESTAMP)
                ON CONFLICT (player_id, field_id) DO UPDATE SET
                    daily_used_seconds  = EXCLUDED.daily_used_seconds,
                    weekly_used_seconds = EXCLUDED.weekly_used_seconds,
                    last_daily_reset    = EXCLUDED.last_daily_reset,
                    last_weekly_reset   = EXCLUDED.last_weekly_reset,
                    last_saved_at       = CURRENT_TIMESTAMP";

            var affected = await connection.ExecuteAsync(sql, new
            {
                PlayerId           = playerId,
                FieldId            = fieldId,
                DailyUsedSeconds   = dailyUsedSeconds,
                WeeklyUsedSeconds  = weeklyUsedSeconds,
                LastDailyReset     = lastDailyReset.Date,
                LastWeeklyReset    = lastWeeklyReset.Date
            });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SaveFieldQuota failed: playerId={PlayerId}, fieldId={FieldId}", playerId, fieldId);
            return false;
        }
    }
}
