using GameServer.Database.Repositories;
using Npgsql;
using Serilog;

namespace GameServer.Database;

/// <summary>
/// Centralized database manager
/// </summary>
public class DatabaseManager
{
    private static DatabaseManager? _instance;
    public static DatabaseManager Instance => _instance ??= new DatabaseManager();

    public AuthRepository Auth { get; private set; } = null!;
    public CommonRepository Common { get; private set; } = null!;
    public GameRepository Game { get; private set; } = null!;

    private DatabaseConfig? _config;

    private DatabaseManager()
    {
    }

    public void Initialize(DatabaseConfig config)
    {
        _config = config;
        Auth   = new AuthRepository(config.AuthConnectionString);
        Common = new CommonRepository(config.CommonConnectionString);
        Game   = new GameRepository(config.GameConnectionString);

        Log.Information("DatabaseManager initialized");
        Log.Information("  Auth DB: {AuthConnection}",    MaskPassword(config.AuthConnectionString));
        Log.Information("  Common DB: {CommonConnection}", MaskPassword(config.CommonConnectionString));
        Log.Information("  Game DB: {GameConnection}",    MaskPassword(config.GameConnectionString));
    }

    /// <summary>
    /// 각 DB에 SELECT 1 쿼리를 보내 연결 상태를 확인합니다.
    /// 연결 실패 시 Warning만 기록하고, 전체 결과를 bool로 반환합니다.
    /// </summary>
    public async Task<bool> CheckHealthAsync()
    {
        if (_config == null)
        {
            Log.Warning("[DB Health] DatabaseManager가 초기화되지 않았습니다");
            return false;
        }

        Log.Information("[DB Health] 데이터베이스 연결 상태 확인 중...");

        var checks = new[]
        {
            ("Auth",   _config.AuthConnectionString),
            ("Common", _config.CommonConnectionString),
            ("Game",   _config.GameConnectionString),
        };

        bool allHealthy = true;
        foreach (var (name, connStr) in checks)
        {
            bool ok = await PingAsync(name, connStr);
            if (!ok) allHealthy = false;
        }

        if (allHealthy)
            Log.Information("[DB Health] 모든 데이터베이스 연결 정상");
        else
            Log.Warning("[DB Health] 일부 DB에 연결할 수 없습니다 — 관련 기능이 동작하지 않을 수 있습니다");

        return allHealthy;
    }

    private static async Task<bool> PingAsync(string dbName, string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            Log.Information("[DB Health]   ✓ {DbName} — 연결 OK", dbName);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("[DB Health]   ✗ {DbName} — 연결 실패: {Message}", dbName, ex.Message);
            return false;
        }
    }

    private static string MaskPassword(string connectionString)
    {
        var parts = connectionString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                parts[i] = "Password=****";
        }
        return string.Join(';', parts);
    }
}
