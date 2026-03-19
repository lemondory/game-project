using GameServer.Database;
using GameServer.Game.Zones;
using GameServer.Network;
using GameShared.Data;
using Serilog;

namespace GameServer;

class Program
{
    static async Task Main(string[] args)
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
                string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../data/bytes");
                if (Directory.Exists(dataPath))
                {
                    GameDataManager.Instance.Initialize(dataPath);
                    Log.Information("MonsterData count: {Count}", GameDataManager.MonsterData.Count);
                    foreach (var monster in GameDataManager.MonsterData.GetAll())
                    {
                        Log.Information("  Monster: ID={Id}, Name={Name}, HP={Hp}",
                            monster.MonsterId, monster.Name, monster.Hp);
                    }
                }
                else
                {
                    Log.Warning("Data directory not found: {Path}", dataPath);
                    Log.Warning("Game data not loaded. Monster/character stats will use fallback values.");
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

            // DB 하트비트 체크 — 하나라도 연결 실패 시 서버 시작 중단 (fail fast)
            bool dbHealthy = await DatabaseManager.Instance.CheckHealthAsync();
            if (!dbHealthy)
            {
                Log.Fatal("데이터베이스에 연결할 수 없어 서버를 시작할 수 없습니다. DB 상태를 확인하세요.");
                return;
            }

            // Initialize Zone Manager and Town Zone
            Log.Information("Initializing zones...");
            ZoneManager.Instance.Initialize();

            int port = 7777;
            TcpServer server = new(port);
            PacketHandler packetHandler = new(server);

            server.Start();

            Log.Information("Server is running on port {Port}. Press Ctrl+C to stop.", port);

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
                    Log.Debug("Processed {Count} packets", processedCount);

                Thread.Sleep(10);
            }

            Log.Information("Shutting down server...");
            server.Stop();

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
