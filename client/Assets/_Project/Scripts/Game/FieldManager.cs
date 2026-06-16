using UnityEngine;
using UnityEngine.InputSystem;
using GameShared.Proto;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Field 씬의 엔티티를 관리하는 싱글톤.
/// NetworkManager.PendingEnterFieldResult에서 초기 데이터를 읽어 초기화한다.
/// 인스턴스 던전과 달리 영구 공유 존이므로 클리어 개념이 없다.
/// </summary>
public class FieldManager : MonoBehaviour
{
    public static FieldManager Instance { get; private set; }

    [Header("프리팹")]
    public GameObject myPlayerPrefab;
    public GameObject otherPlayerPrefab;
    public GameObject monsterPrefab;

    [Header("카메라")]
    public CameraFollow cameraFollow;

    [Header("HUD")]
    public FieldHUD fieldHUD;

    // 엔티티 추적
    private readonly Dictionary<long, GameObject> _entities = new();
    private readonly HashSet<long> _monsterEntityIds = new();

    // 채집 오브젝트 추적
    private readonly Dictionary<int, FieldCollectible> _collectibles = new();

    public long MyEntityId { get; private set; }
    public bool IsDead     { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        var data = NetworkManager.Instance.PendingEnterFieldResult;
        if (data == null)
        {
            Debug.LogError("FieldManager: No PendingEnterFieldResult!");
            return;
        }

        Initialize(data);

        NetworkManager.Instance.OnSpawnReceived            += OnSpawn;
        NetworkManager.Instance.OnDespawnReceived          += OnDespawn;
        NetworkManager.Instance.OnMoveReceived             += OnMove;
        NetworkManager.Instance.OnDamageReceived           += OnDamage;
        NetworkManager.Instance.OnDeathReceived            += OnDeath;
        NetworkManager.Instance.OnRewardReceived           += OnReward;
        NetworkManager.Instance.OnLevelUpReceived          += OnLevelUp;
        NetworkManager.Instance.OnLeaveFieldReceived       += OnLeaveField;
        NetworkManager.Instance.OnFieldQuotaUpdateReceived += OnFieldQuotaUpdate;
        NetworkManager.Instance.OnRespawnResultReceived    += OnRespawnResult;
        NetworkManager.Instance.OnObjectInfoReceived       += OnObjectInfo;
        NetworkManager.Instance.OnObjectStateReceived      += OnObjectState;
        NetworkManager.Instance.OnInteractResultReceived   += OnInteractResult;
    }

    void Update()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            LeaveField();
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnSpawnReceived            -= OnSpawn;
        NetworkManager.Instance.OnDespawnReceived          -= OnDespawn;
        NetworkManager.Instance.OnMoveReceived             -= OnMove;
        NetworkManager.Instance.OnDamageReceived           -= OnDamage;
        NetworkManager.Instance.OnDeathReceived            -= OnDeath;
        NetworkManager.Instance.OnRewardReceived           -= OnReward;
        NetworkManager.Instance.OnLevelUpReceived          -= OnLevelUp;
        NetworkManager.Instance.OnLeaveFieldReceived       -= OnLeaveField;
        NetworkManager.Instance.OnFieldQuotaUpdateReceived -= OnFieldQuotaUpdate;
        NetworkManager.Instance.OnRespawnResultReceived    -= OnRespawnResult;
        NetworkManager.Instance.OnObjectInfoReceived       -= OnObjectInfo;
        NetworkManager.Instance.OnObjectStateReceived      -= OnObjectState;
        NetworkManager.Instance.OnInteractResultReceived   -= OnInteractResult;
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    private void Initialize(S2C_EnterFieldResult data)
    {
        MyEntityId = data.EntityId;
        var spawnPos = ToUnity(data.Position);

        var prefab = myPlayerPrefab != null ? myPlayerPrefab : otherPlayerPrefab;
        if (prefab != null)
        {
            var myObj = Instantiate(prefab, spawnPos, Quaternion.identity);
            myObj.name = "MyPlayer";
            _entities[MyEntityId] = myObj;

            if (cameraFollow != null)
                cameraFollow.target = myObj.transform;
        }

        foreach (var entity in data.NearbyEntities)
            SpawnEntity(entity);

        // 쿼터 HUD 초기화
        if (fieldHUD != null)
            fieldHUD.UpdateQuota(data.DailyRemainingSeconds, data.WeeklyRemainingSeconds);

        Debug.Log($"FieldManager: Initialized. EntityId={MyEntityId}, nearby={data.NearbyEntities.Count}, dailyRemaining={data.DailyRemainingSeconds}s");
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

    private void OnDamage(S2C_Damage packet)
    {
        if (_entities.TryGetValue(packet.TargetEntityId, out var targetObj))
        {
            var healthBar = targetObj.GetComponentInChildren<WorldHealthBar>(true);
            if (healthBar != null)
                healthBar.SetHealth(packet.CurrentHp, packet.MaxHp);
        }

        if (packet.TargetEntityId == MyEntityId && fieldHUD != null)
        {
            fieldHUD.SetHealth(packet.CurrentHp, packet.MaxHp);
            fieldHUD.ShowDamage(packet.Damage);
        }
    }

    private void OnDeath(S2C_Death packet)
    {
        if (packet.EntityId == MyEntityId)
        {
            IsDead = true;
            if (fieldHUD != null) fieldHUD.ShowPlayerDeath();
        }
        else if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            ClearTarget(packet.EntityId);
            Destroy(obj);
            _entities.Remove(packet.EntityId);
            _monsterEntityIds.Remove(packet.EntityId);
        }
    }

    private void OnReward(S2C_RewardResult packet)
    {
        if (fieldHUD != null)
        {
            fieldHUD.ShowReward(packet.ExpReward, packet.GoldReward);
            fieldHUD.SetExp(packet.TotalExp, 0);
            fieldHUD.SetGold(packet.TotalGold);
        }
    }

    private void OnLevelUp(S2C_LevelUp packet)
    {
        if (fieldHUD != null)
        {
            fieldHUD.ShowLevelUp(packet.NewLevel);
            fieldHUD.SetLevel(packet.NewLevel);
            fieldHUD.SetHealth(packet.NewMaxHp, packet.NewMaxHp);
        }
    }

    private void OnLeaveField()
    {
        Debug.Log("[FieldManager] LeaveField received — returning to town");
        fieldHUD?.StopQuota();
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadScene("Town");
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("Town");
    }

    private void OnRespawnResult(S2C_RespawnResult packet)
    {
        if (!packet.Success) return;

        IsDead = false;
        fieldHUD?.HideDeathPanel();

        if (_entities.TryGetValue(MyEntityId, out var myObj))
            myObj.transform.position = ToUnity(packet.Position);

        fieldHUD?.SetHealth(packet.CurrentHp, packet.MaxHp);

        Debug.Log($"[FieldManager] Respawned at entrance. HP={packet.CurrentHp}/{packet.MaxHp}");
    }

    private void OnFieldQuotaUpdate(S2C_FieldQuotaUpdate packet)
    {
        if (fieldHUD != null)
            fieldHUD.UpdateQuota(packet.DailyRemainingSeconds, packet.WeeklyRemainingSeconds);

        // 쿼터 0이면 곧 서버에서 강제 퇴장 패킷이 오므로 UI만 갱신
        if (packet.DailyRemainingSeconds <= 0 || packet.WeeklyRemainingSeconds <= 0)
            Debug.Log("[FieldManager] Quota exhausted — waiting for server eviction");
    }

    // ── 퇴장 / 부활 ─────────────────────────────────────────────────────────

    public void LeaveField()
    {
        NetworkManager.Instance.SendLeaveField();
        Debug.Log("[FieldManager] LeaveField requested");
    }

    public void RequestRespawn()
    {
        NetworkManager.Instance.SendRespawn();
        Debug.Log("[FieldManager] Respawn requested");
    }

    public void RequestInteract(int objectId)
    {
        NetworkManager.Instance.SendInteract(objectId);
    }

    // ── WorldObject 핸들러 ───────────────────────────────────────────────────

    private void OnObjectInfo(S2C_ObjectInfo packet)
    {
        if (_collectibles.TryGetValue(packet.ObjectId, out var existing))
        {
            existing.SetState(packet.State);
            return;
        }

        // 프리미티브 구체로 채집 오브젝트 생성 (추후 prefab으로 교체)
        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = $"Collectible_{packet.ObjectId}";
        obj.transform.position = new Vector3(packet.Position.X, packet.Position.Y + 0.5f, packet.Position.Z);
        obj.transform.localScale = Vector3.one * 0.6f;

        // 콜라이더 제거 (물리 충돌 불필요)
        var col = obj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var collectible = obj.AddComponent<FieldCollectible>();
        collectible.Initialize(packet.ObjectId, packet.State);

        _collectibles[packet.ObjectId] = collectible;
    }

    private void OnObjectState(S2C_ObjectState packet)
    {
        if (_collectibles.TryGetValue(packet.ObjectId, out var collectible))
            collectible.SetState(packet.State);
    }

    private void OnInteractResult(S2C_InteractResult packet)
    {
        if (!packet.Success)
        {
            Debug.Log($"[FieldManager] Interact failed: {packet.Message}");
            return;
        }

        if (fieldHUD != null && packet.Reward != null)
        {
            fieldHUD.ShowReward(packet.Reward.ExpReward, 0);
            // TODO: 인벤토리 시스템 연동 시 아이템 추가 처리
            Debug.Log($"[FieldManager] Harvested: itemId={packet.Reward.ItemId} x{packet.Reward.ItemCount}, exp={packet.Reward.ExpReward}");
        }
    }

    // ── 공격 타겟 ────────────────────────────────────────────────────────────

    private long _currentTargetId = -1;
    private GameObject _targetIndicator;

    public long FindNearestMonster(Vector3 playerPosition, float maxRange)
    {
        long nearestId = -1;
        float nearestSq = maxRange * maxRange;

        foreach (long id in _monsterEntityIds)
        {
            if (!_entities.TryGetValue(id, out var obj) || obj == null) continue;
            float sq = (obj.transform.position - playerPosition).sqrMagnitude;
            if (sq < nearestSq) { nearestSq = sq; nearestId = id; }
        }
        return nearestId;
    }

    public GameObject GetEntityObject(long entityId)
    {
        _entities.TryGetValue(entityId, out var obj);
        return obj;
    }

    public void SetTarget(long entityId)
    {
        if (_currentTargetId == entityId) return;
        _currentTargetId = entityId;

        if (_targetIndicator != null) Destroy(_targetIndicator);
        if (!_entities.TryGetValue(entityId, out var targetObj)) return;

        _targetIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _targetIndicator.name = "TargetIndicator";
        _targetIndicator.transform.SetParent(targetObj.transform);
        _targetIndicator.transform.localPosition = new Vector3(0f, -0.49f, 0f);
        _targetIndicator.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);

        var col = _targetIndicator.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var rend = _targetIndicator.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0.5f, 0f, 1f); // 주황색 (던전과 구분)
            rend.material = mat;
        }
    }

    private void ClearTarget(long entityId)
    {
        if (_currentTargetId != entityId) return;
        _currentTargetId = -1;
        if (_targetIndicator != null) { Destroy(_targetIndicator); _targetIndicator = null; }
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────

    private void SpawnEntity(EntityInfo info)
    {
        if (_entities.ContainsKey(info.EntityId)) return;

        var pos = ToUnity(info.Position);
        bool isMonster = info.EntityType == EntityType.Monster;

        GameObject prefab;
        if (isMonster)          prefab = monsterPrefab != null ? monsterPrefab : otherPlayerPrefab;
        else if (info.EntityId == MyEntityId) prefab = myPlayerPrefab;
        else                    prefab = otherPlayerPrefab;

        if (prefab == null) return;

        var obj = Instantiate(prefab, pos, Quaternion.identity);
        obj.name = $"{info.Name}_{info.EntityId}";

        var label = obj.GetComponent<EntityLabel>();
        if (label != null) label.SetName(info.Name);

        if (isMonster)
        {
            _monsterEntityIds.Add(info.EntityId);
            var hpBar = obj.GetComponentInChildren<WorldHealthBar>();
            if (hpBar != null) hpBar.SetHealth(info.CurrentHp, info.MaxHp);
        }

        _entities[info.EntityId] = obj;
    }

    private static Vector3 ToUnity(Vec3 v)
    {
        if (v == null) return Vector3.zero;
        return new Vector3(v.X, v.Y, v.Z);
    }
}
