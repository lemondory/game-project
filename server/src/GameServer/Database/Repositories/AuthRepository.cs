using Dapper;
using GameServer.Database.Models;
using Npgsql;
using Serilog;

namespace GameServer.Database.Repositories;

/// <summary>
/// Auth database repository
/// </summary>
public class AuthRepository
{
    private readonly string _connectionString;

    public AuthRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Get account by username
    /// </summary>
    public async Task<Account?> GetAccountByUsernameAsync(string username)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT account_id as AccountId, username as Username, email as Email,
                       password_hash as PasswordHash, created_at as CreatedAt,
                       last_login_at as LastLoginAt, is_active as IsActive,
                       is_banned as IsBanned, ban_reason as BanReason, ban_until as BanUntil
                FROM accounts
                WHERE username = @Username";

            return await connection.QuerySingleOrDefaultAsync<Account>(sql, new { Username = username });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get account by username: {Username}", username);
            return null;
        }
    }

    /// <summary>
    /// Get account by ID
    /// </summary>
    public async Task<Account?> GetAccountByIdAsync(long accountId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT account_id as AccountId, username as Username, email as Email,
                       password_hash as PasswordHash, created_at as CreatedAt,
                       last_login_at as LastLoginAt, is_active as IsActive,
                       is_banned as IsBanned, ban_reason as BanReason, ban_until as BanUntil
                FROM accounts
                WHERE account_id = @AccountId";

            return await connection.QuerySingleOrDefaultAsync<Account>(sql, new { AccountId = accountId });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get account by ID: {AccountId}", accountId);
            return null;
        }
    }

    /// <summary>
    /// Update last login time
    /// </summary>
    public async Task<bool> UpdateLastLoginAsync(long accountId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE accounts
                SET last_login_at = CURRENT_TIMESTAMP
                WHERE account_id = @AccountId";

            var affected = await connection.ExecuteAsync(sql, new { AccountId = accountId });
            return affected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update last login: {AccountId}", accountId);
            return false;
        }
    }

    /// <summary>
    /// Create new account
    /// </summary>
    public async Task<long?> CreateAccountAsync(string username, string email, string passwordHash)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO accounts (username, email, password_hash)
                VALUES (@Username, @Email, @PasswordHash)
                RETURNING account_id";

            return await connection.QuerySingleAsync<long>(sql, new
            {
                Username = username,
                Email = email,
                PasswordHash = passwordHash
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create account: {Username}", username);
            return null;
        }
    }
}
