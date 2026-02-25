using GameClient.Network;
using GameShared.Enums;
using GameShared.Packets;
using GameShared.Utils;
using MessagePack;
using Serilog;

namespace GameClient;

class Program
{
    static NetworkManager? _network;
    static bool _isLoggedIn = false;
    static string _playerName = string.Empty;

    static async Task Main(string[] args)
    {
        // Initialize Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        // 봇 모드: dotnet run -- bot <count>
        if (args.Length >= 1 && args[0].Equals("bot", StringComparison.OrdinalIgnoreCase))
        {
            int count = args.Length >= 2 && int.TryParse(args[1], out int n) ? n : 1;
            await RunBotsAsync(count);
            return;
        }

        Log.Information("=== Game Client ===");

        _network = new NetworkManager();
        _network.OnPacketReceived += OnPacketReceived;

        Console.WriteLine("Connecting to server...");
        if (!_network.Connect("127.0.0.1", 7777))
        {
            Console.WriteLine("Failed to connect to server.");
            return;
        }

        Console.WriteLine("Connected! Type your username to login:");
        string? username = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(username))
        {
            Console.WriteLine("Invalid username.");
            return;
        }

        // Send login request
        _network.Send(PacketId.C2S_Login, new C2S_Login
        {
            Username = username,
            Password = "test"
        });

        Console.WriteLine("Waiting for login response...");

        // Main loop
        bool isRunning = true;
        Task<string?> inputTask = Task.Run(() => Console.ReadLine());

        while (isRunning)
        {
            if (!_network.IsConnected)
            {
                Console.WriteLine("Disconnected from server.");
                break;
            }

            if (inputTask.IsCompleted)
            {
                string? input = inputTask.Result;
                if (!string.IsNullOrWhiteSpace(input))
                {
                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    {
                        isRunning = false;
                        continue;
                    }

                    if (!_isLoggedIn)
                    {
                        Console.WriteLine("Not logged in yet. Waiting for server response...");
                    }
                    else
                    {
                        ProcessCommand(input);
                    }
                }

                inputTask = Task.Run(() => Console.ReadLine());
            }

            Thread.Sleep(100);
        }

        _network.Disconnect();
        Log.Information("Client shutting down.");
        Log.CloseAndFlush();
    }

    static void ProcessCommand(string input)
    {
        if (_network == null || !_network.IsConnected)
            return;

        string[] parts = input.Split(' ', 2);
        string command = parts[0].ToLower();

        switch (command)
        {
            case "chat":
            case "c":
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: chat <message>");
                    break;
                }
                _network.Send(PacketId.C2S_Chat, new C2S_Chat { Message = parts[1] });
                break;

            case "move":
            case "m":
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: move <x> <y> <z>");
                    break;
                }
                string[] coords = parts[1].Split(' ');
                if (coords.Length == 3 &&
                    float.TryParse(coords[0], out float x) &&
                    float.TryParse(coords[1], out float y) &&
                    float.TryParse(coords[2], out float z))
                {
                    _network.Send(PacketId.C2S_Move, new C2S_Move
                    {
                        Destination = new Vector3(x, y, z)
                    });
                    Console.WriteLine($"Moving to ({x}, {y}, {z})");
                }
                else
                {
                    Console.WriteLine("Invalid coordinates.");
                }
                break;

            case "town":
                _network.Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
                Console.WriteLine("Requesting to enter town...");
                break;

            case "dungeon":
                if (parts.Length < 2 || !int.TryParse(parts[1], out int dungeonId))
                {
                    Console.WriteLine("Usage: dungeon <dungeonId>");
                    break;
                }
                _network.Send(PacketId.C2S_EnterDungeon, new C2S_EnterDungeon { DungeonId = dungeonId });
                Console.WriteLine($"Requesting to enter dungeon {dungeonId}...");
                break;

            case "attack":
            case "a":
                if (parts.Length < 2 || !long.TryParse(parts[1], out long targetId))
                {
                    Console.WriteLine("Usage: attack <targetEntityId>");
                    break;
                }
                _network.Send(PacketId.C2S_Attack, new C2S_Attack { TargetEntityId = targetId });
                Console.WriteLine($"Attacking entity {targetId}...");
                break;

            case "help":
            case "h":
                ShowHelp();
                break;

            default:
                Console.WriteLine($"Unknown command: {command}. Type 'help' for commands.");
                break;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("\n=== Available Commands ===");
        Console.WriteLine("chat/c <message>       - Send a chat message");
        Console.WriteLine("move/m <x> <y> <z>     - Move to coordinates");
        Console.WriteLine("town                   - Enter town");
        Console.WriteLine("dungeon <id>           - Enter dungeon");
        Console.WriteLine("attack/a <entityId>    - Attack target");
        Console.WriteLine("help/h                 - Show this help");
        Console.WriteLine("quit                   - Exit client");
        Console.WriteLine("========================\n");
    }

    static void OnPacketReceived(PacketId packetId, byte[] data)
    {
        try
        {
            switch (packetId)
            {
                case PacketId.S2C_LoginResult:
                    var loginResult = MessagePackSerializer.Deserialize<S2C_LoginResult>(data);
                    if (loginResult.Success)
                    {
                        _isLoggedIn = true;
                        _playerName = loginResult.PlayerName;
                        Console.WriteLine($"[LOGIN SUCCESS] Welcome, {loginResult.PlayerName}! (ID: {loginResult.PlayerId})");

                        // Auto-enter town
                        Console.WriteLine("Entering town...");
                        _network?.Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
                    }
                    else
                    {
                        Console.WriteLine($"[LOGIN FAILED] {loginResult.Message}");
                    }
                    break;

                case PacketId.S2C_Chat:
                    var chat = MessagePackSerializer.Deserialize<S2C_Chat>(data);
                    Console.WriteLine($"[CHAT] {chat.SenderName}: {chat.Message}");
                    break;

                case PacketId.S2C_Move:
                    var move = MessagePackSerializer.Deserialize<S2C_Move>(data);
                    Console.WriteLine($"[MOVE] Entity {move.EntityId} → {move.Position}");
                    break;

                case PacketId.S2C_EnterTownResult:
                    var townResult = MessagePackSerializer.Deserialize<S2C_EnterTownResult>(data);
                    if (townResult.Success)
                    {
                        Console.WriteLine($"[TOWN] Entered town! EntityId: {townResult.EntityId}, Position: {townResult.Position}");
                        Console.WriteLine($"[TOWN] {townResult.NearbyEntities.Count} nearby entities");
                        Console.WriteLine("Type 'help' to see available commands.");
                    }
                    else
                    {
                        Console.WriteLine("[TOWN] Failed to enter town");
                    }
                    break;

                case PacketId.S2C_EnterDungeonResult:
                    var dungeonResult = MessagePackSerializer.Deserialize<S2C_EnterDungeonResult>(data);
                    if (dungeonResult.Success)
                    {
                        Console.WriteLine($"[DUNGEON] Entered dungeon at {dungeonResult.Position}");
                        Console.WriteLine($"[DUNGEON] {dungeonResult.NearbyEntities.Count} nearby entities");
                    }
                    else
                    {
                        Console.WriteLine($"[DUNGEON] Failed: {dungeonResult.Message}");
                    }
                    break;

                case PacketId.S2C_Spawn:
                    var spawn = MessagePackSerializer.Deserialize<S2C_Spawn>(data);
                    Console.WriteLine($"[SPAWN] {spawn.Entity.EntityType} '{spawn.Entity.Name}' (ID: {spawn.Entity.EntityId})");
                    break;

                case PacketId.S2C_Despawn:
                    var despawn = MessagePackSerializer.Deserialize<S2C_Despawn>(data);
                    Console.WriteLine($"[DESPAWN] Entity {despawn.EntityId} left");
                    break;

                case PacketId.S2C_Attack:
                    var attack = MessagePackSerializer.Deserialize<S2C_Attack>(data);
                    Console.WriteLine($"[COMBAT] Entity {attack.AttackerEntityId} attacked {attack.TargetEntityId}");
                    break;

                case PacketId.S2C_Damage:
                    var damage = MessagePackSerializer.Deserialize<S2C_Damage>(data);
                    Console.WriteLine($"[COMBAT] Entity {damage.TargetEntityId} took {damage.Damage} damage ({damage.CurrentHp}/{damage.MaxHp} HP)");
                    break;

                case PacketId.S2C_Death:
                    var death = MessagePackSerializer.Deserialize<S2C_Death>(data);
                    Console.WriteLine($"[COMBAT] Entity {death.EntityId} was killed by {death.KillerEntityId}");
                    break;

                default:
                    Log.Warning("Unhandled packet: {PacketId}", packetId);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling packet {PacketId}", packetId);
        }
    }

    // ── 봇 모드 ─────────────────────────────────────────────────────────────

    // DB에 있는 테스트 계정 목록 (필요시 추가)
    static readonly string[] BotAccounts = ["testuser1", "testuser2", "alice", "bob"];

    static async Task RunBotsAsync(int count)
    {
        int botCount = Math.Min(count, BotAccounts.Length);
        Log.Information("=== Bot Runner: {Count} bots ===", botCount);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // 봇을 0.5초 간격으로 순차 접속 (서버 부하 분산)
        var tasks = new List<Task>();
        for (int i = 0; i < botCount; i++)
        {
            var bot = new Bot(BotAccounts[i]);
            tasks.Add(bot.RunAsync(cts.Token));
            if (i < botCount - 1)
                await Task.Delay(500, cts.Token);
        }

        Log.Information("All bots running. Press Ctrl+C to stop.");
        await Task.WhenAll(tasks);
        Log.Information("All bots stopped.");
        Log.CloseAndFlush();
    }
}
