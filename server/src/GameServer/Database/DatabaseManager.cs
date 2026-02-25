using GameServer.Database.Repositories;
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

    private DatabaseManager()
    {
    }

    public void Initialize(DatabaseConfig config)
    {
        Auth = new AuthRepository(config.AuthConnectionString);
        Common = new CommonRepository(config.CommonConnectionString);
        Game = new GameRepository(config.GameConnectionString);

        Log.Information("DatabaseManager initialized");
        Log.Information("  Auth DB: {AuthConnection}", MaskPassword(config.AuthConnectionString));
        Log.Information("  Common DB: {CommonConnection}", MaskPassword(config.CommonConnectionString));
        Log.Information("  Game DB: {GameConnection}", MaskPassword(config.GameConnectionString));
    }

    private string MaskPassword(string connectionString)
    {
        // Mask password in connection string for logging
        var parts = connectionString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = "Password=****";
            }
        }
        return string.Join(';', parts);
    }
}
