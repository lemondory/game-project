using System.Collections.Concurrent;
using System.Linq;
using GameServer.Database;
using GameServer.Game.Zones;
using GameShared.Data;
using GameShared.Enums;
using GameShared.Generated.Enums;
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

    // AccountId → 활성 세션 (중복 로그인 감지용, 멀티스레드 접근 가능)
    private readonly ConcurrentDictionary<long, ISession> _loggedInSessions = new();

    public PacketHandler(TcpServer server)
    {
        // 세션 종료 시 매핑 정리
        server.SessionDisconnected += session =>
        {
            _sessionToEntityId.Remove(session.SessionId);
            _sessionToZoneId.Remove(session.SessionId);

            // 로그인 세션 맵에서도 제거 (해당 세션이 등록된 경우에만)
            if (session.PlayerId.HasValue)
            {
                _loggedInSessions.TryRemove(
                    new KeyValuePair<long, ISession>(session.PlayerId.Value, session));
            }
        };

        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        Register(PacketId.C2S_Login,       C2S_Login.Parser,       OnLogin);
        Register(PacketId.C2S_EnterTown,   C2S_EnterTown.Parser,   OnEnterTown);
        Register(PacketId.C2S_EnterDungeon, C2S_EnterDungeon.Parser, OnEnterDungeon);
        Register(PacketId.C2S_LeaveDungeon, C2S_LeaveDungeon.Parser, OnLeaveDungeon);
        Register(PacketId.C2S_Move,         C2S_Move.Parser,         OnMove);
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

            // 중복 로그인 처리: 기존 세션이 있으면 강제 종료
            if (_loggedInSessions.TryGetValue(account.AccountId, out var existingSession))
            {
                Log.Warning("Session {Id}: duplicate login for AccountId={AccountId}, kicking existing session {ExistingId}",
                    session.SessionId, account.AccountId, existingSession.SessionId);

                existingSession.Send(PacketId.S2C_ForceLogout, new S2C_ForceLogout
                    { Message = "다른 곳에서 로그인되었습니다." });
                existingSession.Disconnect();
            }

            session.PlayerId   = account.AccountId;
            session.PlayerName = account.Username;

            // 새 세션을 로그인 맵에 등록
            _loggedInSessions[account.AccountId] = session;

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

            // ViewRadius 이내 기존 엔티티 목록 (거리 필터 적용)
            var nearbyEntities = townZone.GetNearbyEntityInfos(session);

            // AoiSystem 중복 Spawn 방지: 초기 관심 집합 설정
            var interest = entity.Get<Game.Components.InterestComponent>();
            foreach (var e in nearbyEntities)
                interest.VisibleEntityIds.Add(e.EntityId);

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

    private const float PortalInteractRadius = 5f; // 클라이언트(3f)보다 약간 여유 있게

    private void OnEnterDungeon(ISession session, C2S_EnterDungeon packet)
    {
        if (session.PlayerId == null || string.IsNullOrEmpty(session.PlayerName))
        {
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Not logged in" });
            return;
        }

        // ── 포털 거리 검증 ────────────────────────────────────────────────────
        var townZone = ZoneManager.Instance.GetTownZone();
        var playerPos = townZone.GetPlayerPosition(session);
        if (playerPos == null)
        {
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Player not in town" });
            return;
        }

        if (GameDataManager.Instance.IsLoaded)
        {
            var portals = GameDataManager.MapObjectData
                .Where(o => o.ObjectType == ObjectType.DungeonPortal && o.ReferenceId == packet.DungeonId)
                .ToList();

            bool nearPortal = portals.Any(p =>
            {
                float dx = p.PosX - playerPos.Value.X;
                float dz = p.PosZ - playerPos.Value.Z;
                return MathF.Sqrt(dx * dx + dz * dz) <= PortalInteractRadius;
            });

            if (!nearPortal)
            {
                Log.Warning("Session {Id}: EnterDungeon rejected — not near portal (dungeonId={DungeonId}, pos={X},{Z})",
                    session.SessionId, packet.DungeonId, playerPos.Value.X, playerPos.Value.Z);
                session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                    { Success = false, Message = "Not near dungeon portal" });
                return;
            }
        }

        try
        {
            var dungeonZone = ZoneManager.Instance.CreateDungeonZone(packet.DungeonId);
            var entity      = dungeonZone.AddPlayer(session, session.PlayerId.Value, session.PlayerName);
            var entityId    = entity.Get<Game.Components.EntityIdComponent>().EntityId;

            _sessionToEntityId[session.SessionId] = entityId;
            _sessionToZoneId[session.SessionId]   = dungeonZone.ZoneId;

            // 초기 몬스터는 CreateDungeonZone() 내부(게임루프 시작 전)에서 이미 스폰됨
            // ViewRadius 이내 기존 엔티티 목록 (거리 필터 적용)
            var nearbyEntities = dungeonZone.GetNearbyEntityInfos(session);

            // AoiSystem 중복 Spawn 방지: 초기 관심 집합 설정
            var interest = entity.Get<Game.Components.InterestComponent>();
            foreach (var nearby in nearbyEntities)
                interest.VisibleEntityIds.Add(nearby.EntityId);

            var result = new S2C_EnterDungeonResult
            {
                Success  = true,
                Message  = "Entered dungeon",
                EntityId = entityId,
                Position = new Vec3 { X = 0, Y = 0, Z = 0 }
            };
            result.NearbyEntities.AddRange(nearbyEntities);
            session.Send(PacketId.S2C_EnterDungeonResult, result);

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

    private void OnLeaveDungeon(ISession session, C2S_LeaveDungeon packet)
    {
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: LeaveDungeon but not in any zone", session.SessionId);
            return;
        }

        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone is not DungeonZone dungeonZone)
        {
            Log.Warning("Session {Id}: LeaveDungeon but zone {ZoneId} is not a dungeon", session.SessionId, zoneId);
            return;
        }

        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
            return;

        // 던전 파티에서 플레이어 제거
        dungeonZone.RemovePlayer(session.PlayerId ?? 0);

        // 세션 매핑 정리
        _sessionToEntityId.Remove(session.SessionId);
        _sessionToZoneId.Remove(session.SessionId);

        Log.Information("Session {Id}: left dungeon (ZoneId={ZoneId})", session.SessionId, zoneId);

        // 마을 입장 처리
        FireAndForget(OnEnterTownAsync(session, new C2S_EnterTown()));

        // 마지막 플레이어가 나갔으면 던전 삭제
        if (dungeonZone.PartyMembers.Count == 0)
        {
            Log.Information("Dungeon ZoneId={ZoneId} is empty, destroying", zoneId);
            Task.Run(() => ZoneManager.Instance.RemoveZone(zoneId));
        }
    }

    private void OnMove(ISession session, C2S_Move packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
        {
            Log.Warning("Session {Id}: move but no entity", session.SessionId);
            return;
        }

        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: move but no zone", session.SessionId);
            return;
        }

        var dest = packet.Destination;
        if (dest == null || !IsValidCoordinate(dest.X, dest.Y, dest.Z))
        {
            Log.Warning("Session {Id}: invalid move destination", session.SessionId);
            return;
        }

        Log.Debug("Session {Id}: move → ({X},{Y},{Z})", session.SessionId, dest.X, dest.Y, dest.Z);

        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone == null) return;

        var destination = new GameShared.Utils.Vector3 { X = dest.X, Y = dest.Y, Z = dest.Z };
        zone.HandleMove(entityId, destination, 5.0f);
    }

    private void OnChat(ISession session, C2S_Chat packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
        {
            Log.Warning("Session {Id}: chat but no entity", session.SessionId);
            return;
        }

        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: chat but no zone", session.SessionId);
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

        var zone = ZoneManager.Instance.GetZone(zoneId);
        zone?.HandleChat(entityId, message);
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
