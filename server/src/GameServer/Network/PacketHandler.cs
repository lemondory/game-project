using GameServer.Database;
using GameServer.Game.Zones;
using GameShared.Enums;
using GameShared.Proto;
using Google.Protobuf;
using Serilog;

namespace GameServer.Network;

public class PacketHandler
{
    private const int MaxChatLength = 200;
    private const float MaxCoordinate = 10000f;

    private readonly Dictionary<PacketId, Action<ISession, byte[]>> _handlers = new();

    // 세션별 엔티티/존 매핑 (메인 패킷 처리 루프 단일 스레드에서만 접근)
    private readonly Dictionary<long, long> _sessionToEntityId = new();
    private readonly Dictionary<long, int>  _sessionToZoneId   = new();

    public PacketHandler(TcpServer server)
    {
        // 세션 종료 시 매핑 정리
        server.SessionDisconnected += session =>
        {
            _sessionToEntityId.Remove(session.SessionId);
            _sessionToZoneId.Remove(session.SessionId);
        };

        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        Register(PacketId.C2S_Login,       C2S_Login.Parser,       OnLogin);
        Register(PacketId.C2S_EnterTown,   C2S_EnterTown.Parser,   OnEnterTown);
        Register(PacketId.C2S_EnterDungeon,C2S_EnterDungeon.Parser,OnEnterDungeon);
        Register(PacketId.C2S_Move,        C2S_Move.Parser,        OnMove);
        Register(PacketId.C2S_Chat,        C2S_Chat.Parser,        OnChat);
        Register(PacketId.C2S_Attack,      C2S_Attack.Parser,      OnAttack);
    }

    private void Register<T>(PacketId packetId, MessageParser<T> parser, Action<ISession, T> handler)
        where T : IMessage<T>
    {
        _handlers[packetId] = (session, data) =>
        {
            try
            {
                T packet = parser.ParseFrom(data);
                handler(session, packet);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PacketHandler: deserialization failed for {PacketId}", packetId);
            }
        };
    }

    public void Handle(ISession session, PacketId packetId, byte[] data)
    {
        if (_handlers.TryGetValue(packetId, out var handler))
            handler(session, data);
        else
            Log.Warning("PacketHandler: no handler for {PacketId}", packetId);
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnLogin(ISession session, C2S_Login packet)
    {
        FireAndForget(OnLoginAsync(session, packet));
    }

    private async Task OnLoginAsync(ISession session, C2S_Login packet)
    {
        Log.Information("Session {Id}: login request — {Username}", session.SessionId, packet.Username);

        try
        {
            var account = await DatabaseManager.Instance.Auth.GetAccountByUsernameAsync(packet.Username);

            if (account == null)
            {
                session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult
                    { Success = false, Message = "Invalid username or password" });
                return;
            }

            if (account.IsBanned)
            {
                var msg = account.BanUntil.HasValue
                    ? $"Account banned until {account.BanUntil.Value:yyyy-MM-dd HH:mm}"
                    : "Account permanently banned";
                session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult { Success = false, Message = msg });
                return;
            }

            // TODO: BCrypt 패스워드 검증
            // if (!BCrypt.Net.BCrypt.Verify(packet.Password, account.PasswordHash)) { ... }

            await DatabaseManager.Instance.Auth.UpdateLastLoginAsync(account.AccountId);

            session.PlayerId   = account.AccountId;
            session.PlayerName = account.Username;

            session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult
            {
                Success    = true,
                Message    = "Login successful",
                PlayerId   = account.AccountId,
                PlayerName = account.Username
            });

            Log.Information("Session {Id}: login OK — AccountId={AccountId}", session.SessionId, account.AccountId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: login failed", session.SessionId);
            session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult
                { Success = false, Message = "Server error" });
        }
    }

    private void OnEnterTown(ISession session, C2S_EnterTown packet)
    {
        FireAndForget(OnEnterTownAsync(session, packet));
    }

    private async Task OnEnterTownAsync(ISession session, C2S_EnterTown packet)
    {
        Log.Information("Session {Id}: enter town request", session.SessionId);

        if (session.PlayerId == null || string.IsNullOrEmpty(session.PlayerName))
        {
            session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
            return;
        }

        try
        {
            var characters = await DatabaseManager.Instance.Common.GetCharactersByAccountIdAsync(session.PlayerId.Value);
            if (characters.Count == 0)
            {
                Log.Warning("Session {Id}: no characters for account {AccountId}", session.SessionId, session.PlayerId.Value);
                session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
                return;
            }

            var character = characters[0];
            var player    = await DatabaseManager.Instance.Game.GetPlayerByCharacterIdAsync(character.CharacterId);

            if (player == null)
            {
                Log.Information("Session {Id}: creating new player for character {CharacterId}", session.SessionId, character.CharacterId);
                var playerId = await DatabaseManager.Instance.Game.CreatePlayerAsync(
                    character.CharacterId, session.PlayerId.Value,
                    character.CharacterName, character.CharacterClass ?? "Warrior");

                if (playerId == null)
                {
                    session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
                    return;
                }

                player = await DatabaseManager.Instance.Game.GetPlayerByCharacterIdAsync(character.CharacterId);
            }

            if (player == null)
            {
                session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
                return;
            }

            await DatabaseManager.Instance.Game.UpdatePlayerLoginAsync(player.PlayerId);

            var townZone = ZoneManager.Instance.GetTownZone();
            var entity   = townZone.AddPlayer(session, session.PlayerId.Value, player.CharacterName);

            var entityId = entity.Get<Game.Components.EntityIdComponent>().EntityId;
            _sessionToEntityId[session.SessionId] = entityId;
            _sessionToZoneId[session.SessionId]   = townZone.ZoneId;

            var nearbyEntities = townZone.GetNearbyEntityInfos(session);
            var result = new S2C_EnterTownResult
            {
                Success  = true,
                EntityId = entityId,
                Position = new Vec3 { X = player.PositionX, Y = player.PositionY, Z = player.PositionZ }
            };
            result.NearbyEntities.AddRange(nearbyEntities);
            session.Send(PacketId.S2C_EnterTownResult, result);

            Log.Information("Session {Id}: entered town (EntityId={EntityId})", session.SessionId, entityId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: enter town failed", session.SessionId);
            session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
        }
    }

    private void OnEnterDungeon(ISession session, C2S_EnterDungeon packet)
    {
        if (session.PlayerId == null || string.IsNullOrEmpty(session.PlayerName))
        {
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Not logged in" });
            return;
        }

        try
        {
            var dungeonZone = ZoneManager.Instance.CreateDungeonZone(packet.DungeonId);
            var entity      = dungeonZone.AddPlayer(session, session.PlayerId.Value, session.PlayerName);
            var entityId    = entity.Get<Game.Components.EntityIdComponent>().EntityId;

            _sessionToEntityId[session.SessionId] = entityId;
            _sessionToZoneId[session.SessionId]   = dungeonZone.ZoneId;

            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
            {
                Success  = true,
                Message  = "Entered dungeon",
                EntityId = entityId,
                Position = new Vec3 { X = 0, Y = 0, Z = 0 }
            });

            Log.Information("Session {Id}: entered dungeon (EntityId={EntityId}, ZoneId={ZoneId})",
                session.SessionId, entityId, dungeonZone.ZoneId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: enter dungeon failed", session.SessionId);
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Failed to enter dungeon" });
        }
    }

    private void OnMove(ISession session, C2S_Move packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
        {
            Log.Warning("Session {Id}: move but no entity", session.SessionId);
            return;
        }

        var dest = packet.Destination;
        if (dest == null || !IsValidCoordinate(dest.X, dest.Y, dest.Z))
        {
            Log.Warning("Session {Id}: invalid move destination", session.SessionId);
            return;
        }

        Log.Debug("Session {Id}: move → ({X},{Y},{Z})", session.SessionId, dest.X, dest.Y, dest.Z);

        var townZone    = ZoneManager.Instance.GetTownZone();
        var destination = new GameShared.Utils.Vector3 { X = dest.X, Y = dest.Y, Z = dest.Z };
        townZone.HandleMove(entityId, destination, 5.0f);
    }

    private void OnChat(ISession session, C2S_Chat packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
        {
            Log.Warning("Session {Id}: chat but no entity", session.SessionId);
            return;
        }

        var message = packet.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            return;

        if (message.Length > MaxChatLength)
        {
            Log.Warning("Session {Id}: chat message too long ({Len}), ignoring", session.SessionId, message.Length);
            return;
        }

        Log.Information("Session {Id}: chat — {Message}", session.SessionId, message);

        var townZone = ZoneManager.Instance.GetTownZone();
        townZone.HandleChat(entityId, message);
    }

    private void OnAttack(ISession session, C2S_Attack packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId)) return;
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId)) return;

        Log.Information("Session {Id}: attack target {TargetId}", session.SessionId, packet.TargetEntityId);

        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone is DungeonZone dungeonZone)
        {
            var currentTime = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;
            dungeonZone.HandleAttack(entityId, packet.TargetEntityId, currentTime);
        }
        else
        {
            Log.Warning("Session {Id}: attack in non-combat zone", session.SessionId);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static bool IsValidCoordinate(float x, float y, float z) =>
        MathF.Abs(x) <= MaxCoordinate &&
        MathF.Abs(y) <= MaxCoordinate &&
        MathF.Abs(z) <= MaxCoordinate &&
        !float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z);

    /// <summary>
    /// async Task 를 fire-and-forget 으로 실행하되, 예외는 반드시 로그에 기록한다.
    /// async void 대신 이 패턴을 사용해 unhandled exception 크래시를 방지한다.
    /// </summary>
    private static void FireAndForget(Task task)
    {
        task.ContinueWith(
            t => Log.Error(t.Exception?.GetBaseException(), "PacketHandler: unhandled async error"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
