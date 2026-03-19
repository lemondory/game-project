using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameShared.Enums;
using GameShared.Proto;

namespace GameServer.Game.Systems;

/// <summary>
/// Manages per-player Area of Interest (AOI).
/// Runs after MovementSystem so grid positions are current.
/// Emits S2C_Spawn / S2C_Despawn when entities enter or leave a player's view radius.
/// </summary>
public class AoiSystem : ISystem<float>
{
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

    private readonly AoiGrid    _grid;

    // Entities that moved this tick — need grid sync
    private readonly EntitySet _dirtyMovedEntities;

    // All entities with HP — used to build S2C_Spawn packets
    private readonly EntitySet _allEntities;

    // Players with active interest tracking
    private readonly EntitySet _playerEntities;

    public bool IsEnabled { get; set; } = true;

    public AoiSystem(World world, AoiGrid grid)
    {
        _grid = grid;

        _dirtyMovedEntities = world.GetEntities()
            .With<DirtyComponent>()
            .With<PositionComponent>()
            .With<EntityIdComponent>()
            .AsSet();

        _allEntities = world.GetEntities()
            .With<EntityIdComponent>()
            .With<PositionComponent>()
            .With<HealthComponent>()
            .AsSet();

        _playerEntities = world.GetEntities()
            .With<SessionComponent>()
            .With<InterestComponent>()
            .With<PositionComponent>()
            .With<EntityIdComponent>()
            .AsSet();
    }

    public void Update(float deltaTime)
    {
        if (!IsEnabled)
            return;

        // ── Step 1: Sync moved entities into the grid ─────────────────────────
        foreach (var entity in _dirtyMovedEntities.GetEntities())
        {
            if (!entity.IsAlive) continue;
            ref var dirty = ref entity.Get<DirtyComponent>();
            if (!dirty.PositionChanged) continue;

            ref var eid = ref entity.Get<EntityIdComponent>();
            ref var pos = ref entity.Get<PositionComponent>();
            _grid.Update(eid.EntityId, pos.Position.X, pos.Position.Z);
        }

        // ── Step 2: Build entity lookup for Spawn packet construction ─────────
        var entityLookup = new Dictionary<long, Entity>(_allEntities.Count);
        foreach (var entity in _allEntities.GetEntities())
        {
            if (!entity.IsAlive) continue;
            ref var eid = ref entity.Get<EntityIdComponent>();
            entityLookup[eid.EntityId] = entity;
        }

        // ── Step 3: Recalculate each player's interest set ────────────────────
        foreach (var playerEntity in _playerEntities.GetEntities())
        {
            if (!playerEntity.IsAlive) continue;

            ref var selfId  = ref playerEntity.Get<EntityIdComponent>();
            ref var selfPos = ref playerEntity.Get<PositionComponent>();
            var     interest = playerEntity.Get<InterestComponent>();  // class — direct ref
            ref var session  = ref playerEntity.Get<SessionComponent>();

            if (!session.Session.IsConnected) continue;

            float px = selfPos.Position.X;
            float pz = selfPos.Position.Z;

            // Candidate set: grid AABB → precise squared-distance check
            var currentVisible = new HashSet<long>();
            foreach (var candidateId in _grid.GetEntityIdsInRange(px, pz, ViewRadius))
            {
                if (candidateId == selfId.EntityId) continue;
                if (!entityLookup.TryGetValue(candidateId, out var candidate)) continue;
                if (!candidate.IsAlive) continue;

                ref var cPos = ref candidate.Get<PositionComponent>();
                float dx = cPos.Position.X - px;
                float dz = cPos.Position.Z - pz;
                if (dx * dx + dz * dz <= ViewRadiusSq)
                    currentVisible.Add(candidateId);
            }

            // Newly in view → S2C_Spawn
            foreach (var newId in currentVisible)
            {
                if (interest.VisibleEntityIds.Contains(newId)) continue;
                if (!entityLookup.TryGetValue(newId, out var newEntity) || !newEntity.IsAlive) continue;

                session.Session.Send(PacketId.S2C_Spawn, BuildSpawnPacket(newEntity));
                interest.VisibleEntityIds.Add(newId);
            }

            // Left view → S2C_Despawn
            var leftView = new List<long>();
            foreach (var oldId in interest.VisibleEntityIds)
            {
                if (!currentVisible.Contains(oldId))
                    leftView.Add(oldId);
            }
            foreach (var leftId in leftView)
            {
                session.Session.Send(PacketId.S2C_Despawn, new S2C_Despawn { EntityId = leftId });
                interest.VisibleEntityIds.Remove(leftId);
            }
        }
    }

    private static S2C_Spawn BuildSpawnPacket(Entity entity)
    {
        ref var eid    = ref entity.Get<EntityIdComponent>();
        ref var pos    = ref entity.Get<PositionComponent>();
        ref var health = ref entity.Get<HealthComponent>();

        string name;
        GameShared.Proto.EntityType type;

        if (entity.Has<PlayerComponent>())
        {
            name = entity.Get<PlayerComponent>().Name;
            type = GameShared.Proto.EntityType.Player;
        }
        else if (entity.Has<MonsterComponent>())
        {
            ref var monster = ref entity.Get<MonsterComponent>();
            name = $"Monster{monster.MonsterId}";
            type = GameShared.Proto.EntityType.Monster;
        }
        else
        {
            name = string.Empty;
            type = GameShared.Proto.EntityType.Player;
        }

        return new S2C_Spawn
        {
            Entity = new EntityInfo
            {
                EntityId   = eid.EntityId,
                EntityType = type,
                Name       = name,
                Position   = new Vec3 { X = pos.Position.X, Y = pos.Position.Y, Z = pos.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            }
        };
    }

    public void Dispose()
    {
        _dirtyMovedEntities.Dispose();
        _allEntities.Dispose();
        _playerEntities.Dispose();
    }
}
