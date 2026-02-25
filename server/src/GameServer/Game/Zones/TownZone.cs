using DefaultEcs;
using GameServer.Game.Components;
using GameServer.Network;
using GameShared.Enums;
using GameShared.Proto;
using GameShared.Utils;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// Singleton town zone where all players gather.
/// Safe zone — no combat, chat and movement only.
/// </summary>
public class TownZone : Zone
{
    private const int TownZoneId = 1;
    private static long _nextEntityId = 1000;

    // 자주 쓰이는 EntitySet 을 생성자에서 한 번만 만들어 캐싱한다.
    // DefaultEcs 는 World 당 동일 필터의 EntitySet 을 내부적으로 재사용하지 않으므로
    // 반복 생성을 피하기 위해 직접 캐싱한다.
    private readonly EntitySet _entitiesWithId;       // EntityId 보유 엔티티 전체
    private readonly EntitySet _entitiesWithPlayer;   // EntityId + PlayerComponent 보유

    public TownZone() : base(TownZoneId, ZoneType.Town)
    {
        _entitiesWithId     = World.GetEntities().With<EntityIdComponent>().AsSet();
        _entitiesWithPlayer = World.GetEntities().With<EntityIdComponent>().With<PlayerComponent>().AsSet();
    }

    // ── Player Management ────────────────────────────────────────────────────

    /// <summary>플레이어를 마을에 추가하고 기존 플레이어들에게 스폰을 알린다</summary>
    public Entity AddPlayer(ISession session, long playerId, string playerName)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        // 모든 컴포넌트를 한 번에 추가 — 게임루프가 중간 상태를 보지 못하도록
        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new PlayerComponent(playerId, playerName));
        entity.Set(new SessionComponent(session));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(0f, 0f, 0f));
        entity.Set(new HealthComponent(100));

        Log.Information("Player entered town: {PlayerId} — {PlayerName} (EntityId: {EntityId})",
            playerId, playerName, entityId);

        BroadcastSpawn(entity);
        return entity;
    }

    /// <summary>입장 시점의 기존 엔티티 목록을 반환한다 (S2C_EnterTownResult.NearbyEntities 용도)</summary>
    public List<EntityInfo> GetNearbyEntityInfos(ISession excludeSession)
    {
        var result = new List<EntityInfo>();

        foreach (var entity in _entitiesWithId.GetEntities())
        {
            if (!entity.Has<PositionComponent>() || !entity.Has<HealthComponent>())
                continue;

            // 새로 입장하는 플레이어 본인은 제외
            if (entity.Has<SessionComponent>())
            {
                ref var s = ref entity.Get<SessionComponent>();
                if (s.Session == excludeSession) continue;
            }

            ref var eid      = ref entity.Get<EntityIdComponent>();
            ref var position = ref entity.Get<PositionComponent>();
            ref var health   = ref entity.Get<HealthComponent>();
            bool isPlayer    = entity.Has<PlayerComponent>();
            string name      = isPlayer ? entity.Get<PlayerComponent>().Name : string.Empty;

            result.Add(new EntityInfo
            {
                EntityId   = eid.EntityId,
                EntityType = isPlayer ? GameShared.Proto.EntityType.Player : GameShared.Proto.EntityType.Monster,
                Name       = name,
                Position   = new Vec3 { X = position.Position.X, Y = position.Position.Y, Z = position.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            });
        }

        return result;
    }

    // ── Input Handlers (game-loop thread via EnqueueAction) ──────────────────

    public void HandleMove(long entityId, Vector3 destination, float speed)
    {
        EnqueueAction(() =>
        {
            foreach (var entity in _entitiesWithId.GetEntities())
            {
                ref var id = ref entity.Get<EntityIdComponent>();
                if (id.EntityId != entityId) continue;

                entity.Set(new DestinationComponent(destination));
                entity.Set(new VelocityComponent(speed, new Vector3()));
                Log.Debug("Player moving: EntityId={EntityId}, Target=({X},{Y},{Z})",
                    entityId, destination.X, destination.Y, destination.Z);
                break;
            }
        });
    }

    public void HandleChat(long entityId, string message)
    {
        EnqueueAction(() =>
        {
            foreach (var entity in _entitiesWithPlayer.GetEntities())
            {
                ref var id = ref entity.Get<EntityIdComponent>();
                if (id.EntityId != entityId) continue;

                ref var player = ref entity.Get<PlayerComponent>();
                BroadcastChat(player.Name, message);
                break;
            }
        });
    }

    // ── Broadcast Helpers ────────────────────────────────────────────────────

    private void BroadcastSpawn(in Entity newEntity)
    {
        ref var entityId = ref newEntity.Get<EntityIdComponent>();
        ref var position = ref newEntity.Get<PositionComponent>();
        ref var health   = ref newEntity.Get<HealthComponent>();

        bool isPlayer     = newEntity.Has<PlayerComponent>();
        string entityName = isPlayer ? newEntity.Get<PlayerComponent>().Name : string.Empty;

        var packet = new S2C_Spawn
        {
            Entity = new EntityInfo
            {
                EntityId   = entityId.EntityId,
                EntityType = isPlayer ? GameShared.Proto.EntityType.Player : GameShared.Proto.EntityType.Monster,
                Name       = entityName,
                Position   = new Vec3 { X = position.Position.X, Y = position.Position.Y, Z = position.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            }
        };

        // 자신을 제외한 모든 세션에 전송
        var sessions = World.GetEntities().With<SessionComponent>().AsSet();
        foreach (var entity in sessions.GetEntities())
        {
            if (entity == newEntity) continue;
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(PacketId.S2C_Spawn, packet);
        }
    }

    private void BroadcastChat(string playerName, string message)
    {
        var packet = new S2C_Chat { SenderName = playerName, Message = message };

        var sessions = World.GetEntities().With<SessionComponent>().AsSet();
        foreach (var entity in sessions.GetEntities())
        {
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(PacketId.S2C_Chat, packet);
        }

        Log.Information("Chat: [{PlayerName}] {Message}", playerName, message);
    }

    protected override void OnUpdate(float deltaTime) { }

    public override void Dispose()
    {
        _entitiesWithId.Dispose();
        _entitiesWithPlayer.Dispose();
        base.Dispose();
    }
}
