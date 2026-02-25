using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameServer.Game.Systems;
using GameServer.Network;
using GameShared.Enums;
using GameShared.Proto;
using GameShared.Utils;
using Google.Protobuf;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// Instance dungeon zone - one per party
/// Combat enabled, monsters spawn
/// </summary>
public class DungeonZone : Zone
{
    private static long _nextEntityId = 10000; // Start dungeon entities at 10000

    public int DungeonId { get; }
    public List<long> PartyMembers { get; } = new();
    public DateTime CreatedTime { get; }
    private readonly Random _random = new();

    public DungeonZone(int zoneId, int dungeonId) : base(zoneId, ZoneType.Dungeon)
    {
        DungeonId = dungeonId;
        CreatedTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Override to add CombatSystem and MonsterAI for dungeons
    /// </summary>
    protected override ISystem<float> CreateSystems()
    {
        return new SequentialSystem<float>(
            new MonsterAISystem(World),      // AI decides what to do
            new MovementSystem(World),       // Execute movement
            new CombatSystem(World),         // Manage combat state
            new BroadcastSystem(World),      // Sync to clients
            new SessionSystem(World)         // Handle disconnects
        );
    }

    /// <summary>
    /// Add a player to the dungeon
    /// </summary>
    public Entity AddPlayer(ISession session, long playerId, string playerName)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        // Create player entity
        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new PlayerComponent(playerId, playerName));
        entity.Set(new SessionComponent(session));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(0f, 0f, 0f)); // Dungeon entrance
        entity.Set(new HealthComponent(100)); // Player HP
        entity.Set(new AttackComponent(10, 3f, 1f)); // 10 damage, 3m range, 1s cooldown

        PartyMembers.Add(playerId);

        Log.Information("Player entered dungeon: {PlayerId} - {PlayerName} (EntityId: {EntityId}, DungeonId: {DungeonId})",
            playerId, playerName, entityId, DungeonId);

        // Notify existing entities
        BroadcastSpawn(entity);

        // Send existing entities to new player
        SendExistingEntities(session);

        return entity;
    }

    /// <summary>
    /// Spawn a monster with AI
    /// </summary>
    public Entity SpawnMonster(int monsterId, GameShared.Utils.Vector3 position)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new MonsterComponent(monsterId, null!)); // MonsterData will be loaded from GameDataManager
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(position));
        entity.Set(new HealthComponent(50)); // Monster HP
        entity.Set(new AttackComponent(5, 2f, 2f)); // 5 damage, 2m range, 2s cooldown
        entity.Set(new AIComponent(10f, 2f)); // 10m aggro range, 2m attack range
        entity.Set(new CombatStateComponent(false, 0)); // Initial combat state

        Log.Information("Monster spawned: MonsterId={MonsterId}, EntityId={EntityId}, Position={Position}",
            monsterId, entityId, position);

        // Broadcast spawn to all players
        BroadcastSpawn(entity);

        return entity;
    }

    /// <summary>
    /// Handle player attack
    /// </summary>
    public void HandleAttack(long attackerEntityId, long targetEntityId, float currentTime)
    {
        // Find attacker entity
        var entities = World.GetEntities()
            .With<EntityIdComponent>()
            .With<AttackComponent>()
            .AsSet();

        Entity? attackerEntity = null;
        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == attackerEntityId)
            {
                attackerEntity = entity;
                break;
            }
        }

        if (!attackerEntity.HasValue)
        {
            Log.Warning("Attack failed: attacker entity {AttackerEntityId} not found", attackerEntityId);
            return;
        }

        // Check cooldown
        ref var attack = ref attackerEntity.Value.Get<AttackComponent>();
        if (!attack.CanAttack(currentTime))
        {
            Log.Debug("Attack failed: cooldown not ready");
            return;
        }

        // Find target entity
        Entity? targetEntity = null;
        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == targetEntityId)
            {
                targetEntity = entity;
                break;
            }
        }

        if (!targetEntity.HasValue)
        {
            Log.Warning("Attack failed: target entity {TargetEntityId} not found", targetEntityId);
            return;
        }

        // Check range
        ref var attackerPos = ref attackerEntity.Value.Get<PositionComponent>();
        ref var targetPos = ref targetEntity.Value.Get<PositionComponent>();
        float distance = attackerPos.Position.Distance(targetPos.Position);

        if (distance > attack.Range)
        {
            Log.Debug("Attack failed: target out of range ({Distance}m > {Range}m)", distance, attack.Range);
            return;
        }

        // Apply damage
        ref var targetHealth = ref targetEntity.Value.Get<HealthComponent>();
        int damage = attack.Power;
        targetHealth.Current = Math.Max(0, targetHealth.Current - damage);

        // Update last attack time
        attack.LastAttackTime = currentTime;

        Log.Information("Attack: {AttackerEntityId} → {TargetEntityId}, Damage={Damage}, HP={CurrentHp}/{MaxHp}",
            attackerEntityId, targetEntityId, damage, targetHealth.Current, targetHealth.Max);

        // Broadcast attack
        BroadcastAttack(attackerEntityId, targetEntityId);

        // Broadcast damage
        BroadcastDamage(targetEntityId, damage, targetHealth.Current);

        // Check death
        if (targetHealth.IsDead)
        {
            HandleDeath(targetEntity.Value, attackerEntity.Value);
        }
    }

    private void HandleDeath(Entity deadEntity, Entity killerEntity)
    {
        ref var deadEntityId = ref deadEntity.Get<EntityIdComponent>();
        ref var killerEntityId = ref killerEntity.Get<EntityIdComponent>();

        Log.Information("Entity {DeadEntityId} killed by {KillerEntityId}",
            deadEntityId.EntityId, killerEntityId.EntityId);

        // Broadcast death
        BroadcastDeath(deadEntityId.EntityId, killerEntityId.EntityId);

        // Remove dead entity
        deadEntity.Dispose();
    }

    private void BroadcastSpawn(in Entity newEntity)
    {
        ref var entityId = ref newEntity.Get<EntityIdComponent>();
        ref var position = ref newEntity.Get<PositionComponent>();
        ref var health = ref newEntity.Get<HealthComponent>();

        var isPlayer = newEntity.Has<PlayerComponent>();
        string entityName = "";
        GameShared.Proto.EntityType entityType;

        if (isPlayer)
        {
            ref var player = ref newEntity.Get<PlayerComponent>();
            entityName = player.Name;
            entityType = GameShared.Proto.EntityType.Player;
        }
        else if (newEntity.Has<MonsterComponent>())
        {
            ref var monster = ref newEntity.Get<MonsterComponent>();
            entityName = $"Monster{monster.MonsterId}";
            entityType = GameShared.Proto.EntityType.Monster;
        }
        else
        {
            entityType = GameShared.Proto.EntityType.Player; // Default
        }

        var packet = new S2C_Spawn
        {
            Entity = new EntityInfo
            {
                EntityId = entityId.EntityId,
                EntityType = entityType,
                Name = entityName,
                Position = new GameShared.Proto.Vec3 { X = position.Position.X, Y = position.Position.Y, Z = position.Position.Z },
                CurrentHp = health.Current,
                MaxHp = health.Max
            }
        };

        // Send to all players in zone
        var entities = World.GetEntities()
            .With<SessionComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            if (entity == newEntity)
                continue; // Skip self

            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
            {
                session.Session.Send(PacketId.S2C_Spawn, packet);
            }
        }
    }

    private void SendExistingEntities(ISession newPlayerSession)
    {
        var entities = World.GetEntities()
            .With<EntityIdComponent>()
            .With<PositionComponent>()
            .With<HealthComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            // Skip the new player's own entity
            if (entity.Has<SessionComponent>())
            {
                ref var session = ref entity.Get<SessionComponent>();
                if (session.Session == newPlayerSession)
                    continue;
            }

            ref var entityId = ref entity.Get<EntityIdComponent>();
            ref var position = ref entity.Get<PositionComponent>();
            ref var health = ref entity.Get<HealthComponent>();

            var isPlayer = entity.Has<PlayerComponent>();
            string entityName = "";
            GameShared.Proto.EntityType entityType;

            if (isPlayer)
            {
                ref var player = ref entity.Get<PlayerComponent>();
                entityName = player.Name;
                entityType = GameShared.Proto.EntityType.Player;
            }
            else if (entity.Has<MonsterComponent>())
            {
                ref var monster = ref entity.Get<MonsterComponent>();
                entityName = $"Monster{monster.MonsterId}";
                entityType = GameShared.Proto.EntityType.Monster;
            }
            else
            {
                entityType = GameShared.Proto.EntityType.Player;
            }

            var packet = new S2C_Spawn
            {
                Entity = new EntityInfo
                {
                    EntityId = entityId.EntityId,
                    EntityType = entityType,
                    Name = entityName,
                    Position = new GameShared.Proto.Vec3 { X = position.Position.X, Y = position.Position.Y, Z = position.Position.Z },
                    CurrentHp = health.Current,
                    MaxHp = health.Max
                }
            };

            newPlayerSession.Send(PacketId.S2C_Spawn, packet);
        }
    }

    private void BroadcastAttack(long attackerEntityId, long targetEntityId)
    {
        var packet = new S2C_Attack
        {
            AttackerEntityId = attackerEntityId,
            TargetEntityId = targetEntityId
        };

        BroadcastToAllPlayers(PacketId.S2C_Attack, packet);
    }

    private void BroadcastDamage(long targetEntityId, int damage, int currentHp)
    {
        var packet = new S2C_Damage
        {
            TargetEntityId = targetEntityId,
            Damage = damage,
            CurrentHp = currentHp
        };

        BroadcastToAllPlayers(PacketId.S2C_Damage, packet);
    }

    private void BroadcastDeath(long deadEntityId, long killerEntityId)
    {
        var packet = new S2C_Death
        {
            EntityId = deadEntityId,
            KillerEntityId = killerEntityId
        };

        BroadcastToAllPlayers(PacketId.S2C_Death, packet);
    }

    private void BroadcastToAllPlayers(PacketId packetId, IMessage packet)
    {
        var entities = World.GetEntities()
            .With<SessionComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
            {
                session.Session.Send(packetId, packet);
            }
        }
    }

    protected override void OnUpdate(float deltaTime)
    {
        // Dungeon-specific logic
        // TODO: Check dungeon completion, timeout, etc.
    }
}
