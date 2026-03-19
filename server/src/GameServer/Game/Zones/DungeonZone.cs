using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameServer.Game.Systems;
using GameServer.Network;
using GameShared.Data;
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
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

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
    /// Override to add MonsterAI, AoiSystem, and CombatSystem for dungeons.
    /// </summary>
    protected override ISystem<float> CreateSystems()
    {
        return new SequentialSystem<float>(
            new MonsterAISystem(World),
            new MovementSystem(World),
            new AoiSystem(World, AoiGrid),       // AOI: Spawn/Despawn 관리
            new CombatSystem(World,
                onAttack: BroadcastAttack,
                onDamage: (targetId, damage, hp, maxHp) => BroadcastDamage(targetId, damage, hp, maxHp),
                onDeath:  HandleDeath),
            new BroadcastSystem(World),           // S2C_Move (interest 필터 적용)
            new SessionSystem(World, AoiGrid)     // 연결 끊김 정리
        );
    }

    /// <summary>
    /// Add a player to the dungeon. BroadcastSpawn 없음 — AoiSystem이 다음 틱에 처리.
    /// </summary>
    public Entity AddPlayer(ISession session, long playerId, string playerName)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        const int defaultClassId = 1; // Warrior
        var classData = GameDataManager.CharacterClassData.GetById(defaultClassId);
        int hp      = classData?.BaseHp      ?? 120;
        int attack  = classData?.BaseAttack  ?? 10;
        int defense = classData?.BaseDefense ?? 8;

        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new PlayerComponent(playerId, playerName, level: 1, exp: 0, gold: 0, classId: defaultClassId));
        entity.Set(new SessionComponent(session));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(0f, 0f, 0f));
        entity.Set(new HealthComponent(hp));
        entity.Set(new AttackComponent(attack, 3f, 1f));
        entity.Set(new DefenseComponent(defense));
        entity.Set(new InterestComponent());   // AOI interest tracking

        AoiGrid.Add(entityId, 0f, 0f);

        PartyMembers.Add(playerId);

        Log.Information("Player entered dungeon: {PlayerId} - {PlayerName} (EntityId: {EntityId}, DungeonId: {DungeonId})",
            playerId, playerName, entityId, DungeonId);

        // 기존 엔티티를 ViewRadius 이내 것만 전송하고 InterestComponent 초기화
        SendExistingEntitiesInRange(session, entity);

        return entity;
    }

    /// <summary>
    /// Spawn a monster with AI. BroadcastSpawn 없음 — AoiSystem이 다음 틱에 처리.
    /// </summary>
    public Entity SpawnMonster(int monsterId, GameShared.Utils.Vector3 position)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        var monsterData = GameDataManager.MonsterData.GetById(monsterId);
        if (monsterData == null)
        {
            Log.Warning("SpawnMonster: unknown monsterId={MonsterId} — 스폰 취소", monsterId);
            return default;
        }

        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new MonsterComponent(monsterId, monsterData));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(position));
        entity.Set(new HealthComponent(monsterData.Hp));
        entity.Set(new AttackComponent(monsterData.AttackPower, monsterData.AttackRange, monsterData.AttackCooldown));
        entity.Set(new DefenseComponent(monsterData.Defense));
        entity.Set(new AIComponent(monsterData.AggroRange, monsterData.AttackRange, monsterData.MoveSpeed));
        entity.Set(new CombatStateComponent(false, 0));

        // 공간 해시 그리드에 등록 (AoiSystem이 다음 틱에 근처 플레이어에게 S2C_Spawn 전송)
        AoiGrid.Add(entityId, position.X, position.Z);

        Log.Information("Monster spawned: {Name}(Id={MonsterId}) Lv{Level}, EntityId={EntityId}, Pos={Position}",
            monsterData.Name, monsterId, monsterData.Level, entityId, position);

        return entity;
    }

    /// <summary>
    /// Handle player-initiated attack (C2S_Attack packet)
    /// </summary>
    public void HandleAttack(long attackerEntityId, long targetEntityId, float currentTime)
    {
        var entities = World.GetEntities()
            .With<EntityIdComponent>()
            .With<AttackComponent>()
            .AsSet();

        Entity? attackerEntity = null;
        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == attackerEntityId) { attackerEntity = entity; break; }
        }

        if (!attackerEntity.HasValue)
        {
            Log.Warning("Attack failed: attacker entity {AttackerEntityId} not found", attackerEntityId);
            return;
        }

        ref var attack = ref attackerEntity.Value.Get<AttackComponent>();
        if (!attack.CanAttack(currentTime))
        {
            Log.Debug("Attack failed: cooldown not ready");
            return;
        }

        Entity? targetEntity = null;
        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == targetEntityId) { targetEntity = entity; break; }
        }

        if (!targetEntity.HasValue)
        {
            Log.Warning("Attack failed: target entity {TargetEntityId} not found", targetEntityId);
            return;
        }

        ref var attackerPos = ref attackerEntity.Value.Get<PositionComponent>();
        ref var targetPos   = ref targetEntity.Value.Get<PositionComponent>();
        float distance = attackerPos.Position.Distance(targetPos.Position);

        if (distance > attack.Range)
        {
            Log.Debug("Attack failed: target out of range ({Distance}m > {Range}m)", distance, attack.Range);
            return;
        }

        ref var targetHealth = ref targetEntity.Value.Get<HealthComponent>();
        int targetDefense = targetEntity.Value.Has<DefenseComponent>()
            ? targetEntity.Value.Get<DefenseComponent>().Defense
            : 0;
        int damage = Math.Max(1, attack.Power - targetDefense);

        targetHealth.Current  = Math.Max(0, targetHealth.Current - damage);
        attack.LastAttackTime = currentTime;

        Log.Information("Attack: {AttackerEntityId} → {TargetEntityId}, Damage={Damage}, HP={CurrentHp}/{MaxHp}",
            attackerEntityId, targetEntityId, damage, targetHealth.Current, targetHealth.Max);

        BroadcastAttack(attackerEntityId, targetEntityId);
        BroadcastDamage(targetEntityId, damage, targetHealth.Current, targetHealth.Max);

        if (targetHealth.IsDead)
            HandleDeath(targetEntity.Value, attackerEntity.Value);
    }

    private void HandleDeath(Entity deadEntity, Entity killerEntity)
    {
        ref var deadEntityId   = ref deadEntity.Get<EntityIdComponent>();
        ref var killerEntityId = ref killerEntity.Get<EntityIdComponent>();

        Log.Information("Entity {DeadEntityId} killed by {KillerEntityId}",
            deadEntityId.EntityId, killerEntityId.EntityId);

        if (deadEntity.Has<MonsterComponent>()
            && killerEntity.Has<PlayerComponent>()
            && killerEntity.Has<SessionComponent>())
        {
            ref var monster = ref deadEntity.Get<MonsterComponent>();
            ref var player  = ref killerEntity.Get<PlayerComponent>();
            ref var session = ref killerEntity.Get<SessionComponent>();

            int expReward  = monster.Data.ExpReward;
            int goldReward = monster.Data.GoldReward;
            player.Exp  += expReward;
            player.Gold += goldReward;

            int expForNextLevel = player.Level * 100;
            if (player.Exp >= expForNextLevel)
            {
                player.Level++;
                player.Exp -= expForNextLevel;

                var classData = GameDataManager.CharacterClassData.GetById(player.ClassId);
                if (classData != null
                    && killerEntity.Has<HealthComponent>()
                    && killerEntity.Has<AttackComponent>()
                    && killerEntity.Has<DefenseComponent>())
                {
                    ref var health = ref killerEntity.Get<HealthComponent>();
                    ref var atk    = ref killerEntity.Get<AttackComponent>();
                    ref var def    = ref killerEntity.Get<DefenseComponent>();

                    health.Max     += classData.HpPerLevel;
                    health.Current += classData.HpPerLevel;
                    atk.Power      += classData.AttackPerLevel;
                    def.Defense    += classData.DefensePerLevel;

                    session.Session.Send(PacketId.S2C_LevelUp, new S2C_LevelUp
                    {
                        NewLevel   = player.Level,
                        NewMaxHp   = health.Max,
                        NewAttack  = atk.Power,
                        NewDefense = def.Defense
                    });

                    Log.Information("Player {PlayerId} leveled up to {Level}!", player.PlayerId, player.Level);
                }
            }

            session.Session.Send(PacketId.S2C_RewardResult, new S2C_RewardResult
            {
                ExpReward  = expReward,
                GoldReward = goldReward,
                TotalExp   = player.Exp,
                TotalGold  = player.Gold
            });

            Log.Information("Player {PlayerId} earned Exp={Exp}, Gold={Gold}",
                player.PlayerId, expReward, goldReward);
        }

        // S2C_Death: InterestComponent 필터 적용
        BroadcastDeath(deadEntityId.EntityId, killerEntityId.EntityId);

        if (deadEntity.Has<MonsterComponent>())
        {
            // 그리드에서 먼저 제거 → 다음 틱 AoiSystem이 leftView 감지 → S2C_Despawn 전송
            AoiGrid.Remove(deadEntityId.EntityId);
            deadEntity.Dispose();
        }
    }

    // ── Broadcast Helpers ────────────────────────────────────────────────────

    private void BroadcastAttack(long attackerEntityId, long targetEntityId)
    {
        BroadcastToAllPlayers(PacketId.S2C_Attack, new S2C_Attack
        {
            AttackerEntityId = attackerEntityId,
            TargetEntityId   = targetEntityId
        });
    }

    private void BroadcastDamage(long targetEntityId, int damage, int currentHp, int maxHp)
    {
        BroadcastToAllPlayers(PacketId.S2C_Damage, new S2C_Damage
        {
            TargetEntityId = targetEntityId,
            Damage         = damage,
            CurrentHp      = currentHp,
            MaxHp          = maxHp
        });
    }

    /// <summary>S2C_Death는 죽은 엔티티를 실제로 보고 있던 플레이어에게만 전송한다.</summary>
    private void BroadcastDeath(long deadEntityId, long killerEntityId)
    {
        var packet = new S2C_Death { EntityId = deadEntityId, KillerEntityId = killerEntityId };

        var entities = World.GetEntities()
            .With<SessionComponent>()
            .With<InterestComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            var interest = entity.Get<InterestComponent>();
            if (!interest.VisibleEntityIds.Contains(deadEntityId)) continue;

            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(PacketId.S2C_Death, packet);
        }
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
                session.Session.Send(packetId, packet);
        }
    }

    /// <summary>
    /// 새로 입장한 플레이어에게 ViewRadius 이내 기존 엔티티의 S2C_Spawn을 전송하고,
    /// 해당 엔티티 ID들을 새 플레이어의 InterestComponent에 등록한다.
    /// </summary>
    private void SendExistingEntitiesInRange(ISession newPlayerSession, Entity newPlayerEntity)
    {
        ref var newPos   = ref newPlayerEntity.Get<PositionComponent>();
        var     interest = newPlayerEntity.Get<InterestComponent>();

        float px = newPos.Position.X;
        float pz = newPos.Position.Z;

        var entities = World.GetEntities()
            .With<EntityIdComponent>()
            .With<PositionComponent>()
            .With<HealthComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            // 자기 자신 제외
            if (entity.Has<SessionComponent>())
            {
                ref var s = ref entity.Get<SessionComponent>();
                if (s.Session == newPlayerSession) continue;
            }

            // ViewRadius 거리 필터
            ref var pos = ref entity.Get<PositionComponent>();
            float dx = pos.Position.X - px;
            float dz = pos.Position.Z - pz;
            if (dx * dx + dz * dz > ViewRadiusSq) continue;

            ref var entityId = ref entity.Get<EntityIdComponent>();
            ref var health   = ref entity.Get<HealthComponent>();

            string entityName;
            GameShared.Proto.EntityType entityType;

            if (entity.Has<PlayerComponent>())
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
                entityName = string.Empty;
                entityType = GameShared.Proto.EntityType.Player;
            }

            newPlayerSession.Send(PacketId.S2C_Spawn, new S2C_Spawn
            {
                Entity = new EntityInfo
                {
                    EntityId   = entityId.EntityId,
                    EntityType = entityType,
                    Name       = entityName,
                    Position   = new GameShared.Proto.Vec3 { X = pos.Position.X, Y = pos.Position.Y, Z = pos.Position.Z },
                    CurrentHp  = health.Current,
                    MaxHp      = health.Max
                }
            });

            // InterestComponent 초기화 — AoiSystem 중복 Spawn 방지
            interest.VisibleEntityIds.Add(entityId.EntityId);
        }
    }

    /// <summary>
    /// Game loop 스레드에서 안전하게 몬스터를 스폰합니다 (네트워크 스레드에서 호출).
    /// </summary>
    public void EnqueueSpawn(int monsterId, GameShared.Utils.Vector3 position)
        => EnqueueAction(() => SpawnMonster(monsterId, position));

    protected override void OnUpdate(float deltaTime)
    {
        // TODO: Check dungeon completion, timeout, etc.
    }
}
