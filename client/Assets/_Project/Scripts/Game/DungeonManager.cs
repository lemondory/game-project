using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using GameShared.Proto;
using System.Collections.Generic;

/// <summary>
/// Dungeon 씬의 엔티티(플레이어/몬스터)를 관리하는 싱글톤
/// NetworkManager.PendingEnterDungeonResult 에서 초기 데이터를 읽어 초기화한다
/// </summary>
public class DungeonManager : MonoBehaviour
{
    public static DungeonManager Instance { get; private set; }

    [Header("프리팹")]
    public GameObject myPlayerPrefab;
    public GameObject otherPlayerPrefab;
    public GameObject monsterPrefab;

    [Header("카메라")]
    public CameraFollow cameraFollow;

    // 엔티티 추적
    private readonly Dictionary<long, GameObject> _entities = new();
    private readonly HashSet<long> _monsterEntityIds = new();

    public long MyEntityId { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        var data = NetworkManager.Instance.PendingEnterDungeonResult;
        if (data == null)
        {
            Debug.LogError("DungeonManager: No PendingEnterDungeonResult!");
            return;
        }

        Initialize(data);

        NetworkManager.Instance.OnSpawnReceived   += OnSpawn;
        NetworkManager.Instance.OnDespawnReceived += OnDespawn;
        NetworkManager.Instance.OnMoveReceived    += OnMove;
        NetworkManager.Instance.OnDamageReceived  += OnDamage;
        NetworkManager.Instance.OnDeathReceived   += OnDeath;
        NetworkManager.Instance.OnRewardReceived  += OnReward;
        NetworkManager.Instance.OnLevelUpReceived += OnLevelUp;
    }

    void Update()
    {
        // ESC → 마을로 복귀
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            LeaveDungeon();
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnSpawnReceived   -= OnSpawn;
        NetworkManager.Instance.OnDespawnReceived -= OnDespawn;
        NetworkManager.Instance.OnMoveReceived    -= OnMove;
        NetworkManager.Instance.OnDamageReceived  -= OnDamage;
        NetworkManager.Instance.OnDeathReceived   -= OnDeath;
        NetworkManager.Instance.OnRewardReceived  -= OnReward;
        NetworkManager.Instance.OnLevelUpReceived -= OnLevelUp;
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    private void Initialize(S2C_EnterDungeonResult data)
    {
        MyEntityId = data.EntityId;
        var spawnPos = ToUnity(data.Position);

        // 내 플레이어 생성
        var prefab = myPlayerPrefab != null ? myPlayerPrefab : otherPlayerPrefab;
        if (prefab != null)
        {
            var myObj = Instantiate(prefab, spawnPos, Quaternion.identity);
            myObj.name = "MyPlayer";
            _entities[MyEntityId] = myObj;

            if (cameraFollow != null)
                cameraFollow.target = myObj.transform;
        }

        // 주변 엔티티 스폰
        foreach (var entity in data.NearbyEntities)
            SpawnEntity(entity);

        Debug.Log($"DungeonManager: Initialized. EntityId={MyEntityId}, entities={data.NearbyEntities.Count}");
    }

    // ── 패킷 핸들러 ─────────────────────────────────────────────────────────

    private void OnSpawn(S2C_Spawn packet)
    {
        if (packet.Entity == null) return;
        SpawnEntity(packet.Entity);
    }

    private void OnDespawn(S2C_Despawn packet)
    {
        if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            Destroy(obj);
            _entities.Remove(packet.EntityId);
            _monsterEntityIds.Remove(packet.EntityId);
        }
    }

    private void OnMove(S2C_Move packet)
    {
        if (packet.EntityId == MyEntityId) return;

        if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            var mover = obj.GetComponent<EntityMover>();
            if (mover != null)
                mover.SetDestination(ToUnity(packet.Position));
        }
    }

    // ── 내부 헬퍼 ───────────────────────────────────────────────────────────

    private void SpawnEntity(EntityInfo info)
    {
        if (_entities.ContainsKey(info.EntityId)) return;

        var pos = ToUnity(info.Position);

        // EntityType으로 몬스터/플레이어 구분
        bool isMonster = info.EntityType == EntityType.Monster;
        GameObject prefab;
        if (isMonster)
            prefab = monsterPrefab != null ? monsterPrefab : otherPlayerPrefab;
        else if (info.EntityId == MyEntityId)
            prefab = myPlayerPrefab;
        else
            prefab = otherPlayerPrefab;

        if (prefab == null) return;

        var obj = Instantiate(prefab, pos, Quaternion.identity);
        obj.name = $"{info.Name}_{info.EntityId}";

        var label = obj.GetComponent<EntityLabel>();
        if (label != null)
            label.SetName(info.Name);

        // 몬스터 HP바 초기화
        if (isMonster)
        {
            _monsterEntityIds.Add(info.EntityId);
            var hpBar = obj.GetComponentInChildren<WorldHealthBar>();
            if (hpBar != null)
                hpBar.SetHealth(info.CurrentHp, info.MaxHp);
        }

        _entities[info.EntityId] = obj;
    }

    // ── 공격 타겟 ──────────────────────────────────────────────────────────────

    private long _currentTargetId = -1;
    private GameObject _targetIndicator;

    /// <summary>
    /// 지정 위치에서 가장 가까운 몬스터의 EntityId를 반환한다. 범위 내 몬스터가 없으면 -1.
    /// </summary>
    public long FindNearestMonster(Vector3 playerPosition, float maxRange)
    {
        long nearestEntityId = -1;
        float nearestDistanceSq = maxRange * maxRange;

        foreach (long monsterEntityId in _monsterEntityIds)
        {
            if (!_entities.TryGetValue(monsterEntityId, out var monsterObj)) continue;
            if (monsterObj == null) continue;

            float distanceSq = (monsterObj.transform.position - playerPosition).sqrMagnitude;
            if (distanceSq < nearestDistanceSq)
            {
                nearestDistanceSq = distanceSq;
                nearestEntityId = monsterEntityId;
            }
        }

        return nearestEntityId;
    }

    /// <summary>엔티티 ID로 GameObject를 반환한다.</summary>
    public GameObject GetEntityObject(long entityId)
    {
        _entities.TryGetValue(entityId, out var obj);
        return obj;
    }

    /// <summary>공격 대상 몬스터에 타겟 인디케이터를 표시한다.</summary>
    public void SetTarget(long entityId)
    {
        if (_currentTargetId == entityId) return;
        _currentTargetId = entityId;

        // 기존 인디케이터 제거
        if (_targetIndicator != null)
            Destroy(_targetIndicator);

        if (!_entities.TryGetValue(entityId, out var targetObj)) return;

        // 발밑에 빨간 원형 인디케이터 생성
        _targetIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _targetIndicator.name = "TargetIndicator";
        _targetIndicator.transform.SetParent(targetObj.transform);
        _targetIndicator.transform.localPosition = new Vector3(0f, -0.49f, 0f);
        _targetIndicator.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);

        // 콜라이더 제거 (물리 간섭 방지)
        var collider = _targetIndicator.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // 빨간 머티리얼
        var renderer = _targetIndicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0f, 0f, 1f);
            renderer.material = mat;
        }
    }

    /// <summary>타겟이 죽으면 인디케이터를 제거한다.</summary>
    private void ClearTarget(long entityId)
    {
        if (_currentTargetId != entityId) return;
        _currentTargetId = -1;
        if (_targetIndicator != null)
        {
            Destroy(_targetIndicator);
            _targetIndicator = null;
        }
    }

    private void OnDamage(GameShared.Proto.S2C_Damage packet)
    {
        // 몬스터 HP바 업데이트
        if (_entities.TryGetValue(packet.TargetEntityId, out var targetObj))
        {
            var healthBar = targetObj.GetComponentInChildren<WorldHealthBar>(true);
            if (healthBar != null)
                healthBar.SetHealth(packet.CurrentHp, packet.MaxHp);
        }

        // 플레이어 피격 시 HUD 업데이트
        if (packet.TargetEntityId == MyEntityId)
        {
            if (DungeonHUD.Instance != null)
            {
                DungeonHUD.Instance.SetHealth(packet.CurrentHp, packet.MaxHp);
                DungeonHUD.Instance.ShowDamage(packet.Damage);
            }
        }
    }

    private void OnDeath(GameShared.Proto.S2C_Death packet)
    {
        if (packet.EntityId == MyEntityId)
        {
            Debug.Log("[DungeonManager] Player died!");
            if (DungeonHUD.Instance != null)
                DungeonHUD.Instance.ShowPlayerDeath();
        }
        else if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            ClearTarget(packet.EntityId);
            Destroy(obj);
            _entities.Remove(packet.EntityId);
            _monsterEntityIds.Remove(packet.EntityId);
        }
    }

    private void OnReward(GameShared.Proto.S2C_RewardResult packet)
    {
        if (DungeonHUD.Instance != null)
        {
            DungeonHUD.Instance.ShowReward(packet.ExpReward, packet.GoldReward);
            DungeonHUD.Instance.SetExp(packet.TotalExp, 0);
            DungeonHUD.Instance.SetGold(packet.TotalGold);
        }
    }

    private void OnLevelUp(GameShared.Proto.S2C_LevelUp packet)
    {
        if (DungeonHUD.Instance != null)
        {
            DungeonHUD.Instance.ShowLevelUp(packet.NewLevel);
            DungeonHUD.Instance.SetLevel(packet.NewLevel);
            DungeonHUD.Instance.SetHealth(packet.NewMaxHp, packet.NewMaxHp);
        }
    }

    public void LeaveDungeon()
    {
        // 던전 나가기: 마을 재입장 요청
        NetworkManager.Instance.Send(GameShared.Enums.PacketId.C2S_EnterTown,
            new C2S_EnterTown());
        Debug.Log("DungeonManager: Leaving dungeon...");
    }

    private static Vector3 ToUnity(GameShared.Proto.Vec3 v)
    {
        if (v == null) return Vector3.zero;
        return new Vector3(v.X, v.Y, v.Z);
    }
}
