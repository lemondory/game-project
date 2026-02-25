using GameServer.Database;
using GameServer.Game.Zones;
using GameServer.Network;
using GameShared.Data;
using Serilog;

namespace GameServer;

class Program
{
    static void Main(string[] args)
    {
        // Initialize Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/server-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("=== Game Server Starting ===");

        try
        {
            // Initialize game data (optional - skip if not found)
            try
            {
                Log.Information("Loading game data...");
                string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../data/bytes");
                if (Directory.Exists(dataPath))
                {
                    GameDataManager.Instance.Initialize(dataPath);

                    // Test data access
                    Log.Information("Testing data access...");
                    Log.Information("MonsterData count: {Count}", GameDataManager.MonsterData.Count);
                    foreach (var monster in GameDataManager.MonsterData.GetAll())
                    {
                        Log.Information("  Monster: ID={Id}, Name={Name}, HP={Hp}",
                            monster.MonsterId, monster.Name, monster.Hp);
                    }
                }
                else
                {
                    Log.Warning("Data directory not found, skipping data loading. Server will run without game data.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load game data. Server will run without game data.");
            }

            // Initialize Database
            Log.Information("Initializing database...");
            var dbConfig = DatabaseConfig.FromEnvironment();
            DatabaseManager.Instance.Initialize(dbConfig);

            // Initialize Zone Manager and Town Zone
            Log.Information("Initializing zones...");
            ZoneManager.Instance.Initialize();

            int port = 7777;
            TcpServer server = new(port);
            PacketHandler packetHandler = new(server);

            server.Start();

            Log.Information("Server is running. Press Ctrl+C to stop.");

            // Main game loop
            bool isRunning = true;
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                isRunning = false;
                Log.Information("Shutdown signal received...");
            };

            while (isRunning)
            {
                // Process packets from queue
                int processedCount = 0;
                while (server.PacketQueue.TryDequeue(out var message))
                {
                    if (message.Session.IsConnected)
                    {
                        packetHandler.Handle(message.Session, message.PacketId, message.Data);
                        processedCount++;
                    }
                }

                if (processedCount > 0)
                {
                    Log.Debug("Processed {Count} packets", processedCount);
                }

                // Sleep to prevent busy waiting
                Thread.Sleep(10);
            }

            Log.Information("Shutting down server...");
            server.Stop();

            // Shutdown zones
            Log.Information("Shutting down zones...");
            ZoneManager.Instance.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Server crashed");
        }
        finally
        {
            Log.Information("=== Game Server Stopped ===");
            Log.CloseAndFlush();
        }
    }
}
