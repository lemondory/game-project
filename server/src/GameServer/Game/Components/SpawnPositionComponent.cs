using GameShared.Utils;

namespace GameServer.Game.Components;

/// <summary>
/// 몬스터의 최초 스폰 위치. 사망 후 이 위치로 리스폰한다.
/// </summary>
public struct SpawnPositionComponent
{
    public Vector3 Position;

    public SpawnPositionComponent(Vector3 position)
    {
        Position = position;
    }
}
