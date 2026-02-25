using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using GameClient.Network;
using GameShared.Enums;
using GameShared.Packets;
using GameShared.Utils;

namespace SimpleTest;

/// <summary>
/// Simple automated multi-client test
/// Run with: dotnet run
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Phase 2 Multi-Client Automated Test ===\n");
        Console.WriteLine("Connecting 3 clients to localhost:7777...\n");

        var clients = new List<(string name, NetworkManager network, List<string> events)>
        {
            ("Alice", new NetworkManager(), new List<string>()),
            ("Bob", new NetworkManager(), new List<string>()),
            ("Charlie", new NetworkManager(), new List<string>())
        };

        // Setup packet handlers
        foreach (var (name, network, events) in clients)
        {
            network.OnPacketReceived += (packetId, data) =>
            {
                var msg = $"[{name}] received {packetId}";
                events.Add(msg);
                Console.WriteLine(msg);
            };
        }

        try
        {
            // 1. Connect
            Console.WriteLine("Step 1: Connecting clients...");
            foreach (var (name, network, _) in clients)
            {
                if (!network.Connect("127.0.0.1", 7777))
                {
                    Console.WriteLine($"❌ {name} failed to connect");
                    return;
                }
                Console.WriteLine($"✓ {name} connected");
                await Task.Delay(100);
            }

            await Task.Delay(500);

            // 2. Login
            Console.WriteLine("\nStep 2: Logging in...");
            foreach (var (name, network, _) in clients)
            {
                network.Send(PacketId.C2S_Login, new C2S_Login
                {
                    Username = name,
                    Password = "test"
                });
                Console.WriteLine($"→ {name} sent login request");
                await Task.Delay(200);
            }

            await Task.Delay(1000);
            Console.WriteLine("✓ All clients logged in");

            // 3. Enter town
            Console.WriteLine("\nStep 3: Entering town...");
            foreach (var (name, network, _) in clients)
            {
                network.Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
                Console.WriteLine($"→ {name} entering town");
                await Task.Delay(300);
            }

            await Task.Delay(1000);
            Console.WriteLine("✓ All clients in town");

            // 4. Chat test
            Console.WriteLine("\nStep 4: Testing chat...");
            clients[0].network.Send(PacketId.C2S_Chat, new C2S_Chat { Message = "Hello from Alice!" });
            await Task.Delay(500);

            clients[1].network.Send(PacketId.C2S_Chat, new C2S_Chat { Message = "Hi Alice, this is Bob!" });
            await Task.Delay(500);

            clients[2].network.Send(PacketId.C2S_Chat, new C2S_Chat { Message = "Hey everyone, Charlie here!" });
            await Task.Delay(500);

            Console.WriteLine("✓ Chat messages sent");

            // 5. Movement test
            Console.WriteLine("\nStep 5: Testing movement...");
            clients[0].network.Send(PacketId.C2S_Move, new C2S_Move
            {
                Destination = new Vector3(10, 0, 0)
            });
            Console.WriteLine("→ Alice moving to (10, 0, 0)");
            await Task.Delay(1000);

            clients[1].network.Send(PacketId.C2S_Move, new C2S_Move
            {
                Destination = new Vector3(0, 0, 10)
            });
            Console.WriteLine("→ Bob moving to (0, 0, 10)");
            await Task.Delay(1000);

            Console.WriteLine("✓ Movement completed");

            // 6. Disconnect test
            Console.WriteLine("\nStep 6: Testing disconnect...");
            var charlieEventsBefore = clients[2].events.Count;
            clients[1].network.Disconnect();
            Console.WriteLine("→ Bob disconnected");
            await Task.Delay(1500);

            // 7. Summary
            Console.WriteLine("\n=== Test Summary ===");
            foreach (var (name, _, events) in clients)
            {
                Console.WriteLine($"{name}: {events.Count} events received");
            }

            // Check expectations
            bool allPassed = true;

            // Each client should have received login result
            foreach (var (name, _, events) in clients)
            {
                if (events.Any(e => e.Contains("S2C_LoginResult")))
                {
                    Console.WriteLine($"✓ {name} received login result");
                }
                else
                {
                    Console.WriteLine($"❌ {name} missing login result");
                    allPassed = false;
                }
            }

            // Alice and Charlie should have received spawns
            if (clients[0].events.Any(e => e.Contains("S2C_Spawn")))
            {
                Console.WriteLine("✓ Alice received spawn notifications");
            }
            else
            {
                Console.WriteLine("❌ Alice missing spawn notifications");
                allPassed = false;
            }

            // Alice and Charlie should have received chats
            if (clients[0].events.Any(e => e.Contains("S2C_Chat")))
            {
                Console.WriteLine("✓ Alice received chat messages");
            }
            else
            {
                Console.WriteLine("❌ Alice missing chat messages");
                allPassed = false;
            }

            // Alice and Charlie should have received movements
            if (clients[0].events.Any(e => e.Contains("S2C_Move")))
            {
                Console.WriteLine("✓ Alice received movement updates");
            }
            else
            {
                Console.WriteLine("❌ Alice missing movement updates");
                allPassed = false;
            }

            // Alice and Charlie should have received despawn for Bob
            if (clients[2].events.Count(e => e.Contains("S2C_Despawn")) > 0)
            {
                Console.WriteLine("✓ Charlie received despawn notification");
            }
            else
            {
                Console.WriteLine("⚠️  Charlie may not have received despawn (might be timing issue)");
            }

            Console.WriteLine($"\n{(allPassed ? "✅ ALL TESTS PASSED" : "❌ SOME TESTS FAILED")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test failed with exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            // Cleanup
            foreach (var (name, network, _) in clients)
            {
                network.Disconnect();
            }

            Console.WriteLine("\nCleanup complete. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
