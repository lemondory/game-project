using GameShared.Generated.Data;
using GameShared.Utils;

namespace GameServer.Game;

public enum WorldObjectState
{
    Available  = 0,
    Harvested  = 1,
    Respawning = 2,
}

/// <summary>
/// 채집/상호작용 오브젝트. ECS 밖에서 Zone이 직접 관리한다.
/// Available → Harvested → Respawning → Available 순환.
/// </summary>
public class WorldObject
{
    public int          ObjectId   { get; }
    public int          DataId     { get; }
    public Vector3      Position   { get; }
    public WorldObjectState State  { get; private set; }

    private readonly WorldObjectData _data;
    private float _respawnTimer;

    public WorldObject(int objectId, WorldObjectData data, Vector3 position)
    {
        ObjectId = objectId;
        DataId   = data.ObjectId;
        Position = position;
        State    = WorldObjectState.Available;
        _data    = data;
    }

    /// <summary>채집 시도. 성공하면 true를 반환하고 Harvested 상태로 전환한다.</summary>
    public bool TryHarvest()
    {
        if (State != WorldObjectState.Available) return false;

        State         = WorldObjectState.Harvested;
        _respawnTimer = _data.RespawnDelaySeconds;
        return true;
    }

    /// <summary>게임루프 틱. 리스폰 타이머를 갱신하고 Available로 돌아오면 true를 반환한다.</summary>
    public bool Tick(float deltaTime)
    {
        if (State == WorldObjectState.Available) return false;

        State = WorldObjectState.Respawning;
        _respawnTimer -= deltaTime;

        if (_respawnTimer > 0f) return false;

        State = WorldObjectState.Available;
        return true;
    }

    public int RespawnRemainingSeconds => (int)Math.Ceiling(_respawnTimer);

    public int    ExpReward  => _data.ExpReward;
    public int    ItemId     => _data.ItemId;
    public int    ItemCount  => _data.ItemCount;
}
