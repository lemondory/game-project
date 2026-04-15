using GameServer.Network;

namespace GameServer.Game;

/// <summary>
/// 엔티티 ID → 해당 엔티티를 현재 구독 중인 세션 집합의 역방향 인덱스.
///
/// AoiSystem이 Spawn/Despawn 시점에 Subscribe/Unsubscribe를 호출하여 최신 상태를 유지한다.
/// BroadcastSystem은 전체 플레이어를 순회하는 대신 GetSubscribers()로 직접 대상 세션만 조회한다.
///
/// 게임루프 단일 스레드 전용 — 별도 동기화 없음.
/// </summary>
public sealed class SubscriberMap
{
    private readonly Dictionary<long, HashSet<ISession>> _map = new();

    /// <summary>entityId를 구독하는 세션을 추가한다 (AoiSystem: Spawn 시 호출).</summary>
    public void Subscribe(long entityId, ISession session)
    {
        if (!_map.TryGetValue(entityId, out var set))
            _map[entityId] = set = new HashSet<ISession>();
        set.Add(session);
    }

    /// <summary>entityId 구독에서 세션을 제거한다 (AoiSystem: Despawn 시 호출).</summary>
    public void Unsubscribe(long entityId, ISession session)
    {
        if (!_map.TryGetValue(entityId, out var set))
            return;
        set.Remove(session);
        if (set.Count == 0)
            _map.Remove(entityId);
    }

    /// <summary>플레이어 세션이 연결 해제될 때 해당 세션이 구독 중인 모든 항목을 일괄 제거한다.</summary>
    public void UnsubscribeAll(ISession session, IEnumerable<long> subscribedEntityIds)
    {
        foreach (var entityId in subscribedEntityIds)
            Unsubscribe(entityId, session);
    }

    /// <summary>엔티티 자체가 제거될 때(사망/디스폰) 해당 엔티티의 구독자 목록을 삭제한다.</summary>
    public void RemoveEntity(long entityId) => _map.Remove(entityId);

    /// <summary>entityId를 구독 중인 세션 목록을 반환한다. 없으면 빈 컬렉션.</summary>
    public IReadOnlyCollection<ISession> GetSubscribers(long entityId)
        => _map.TryGetValue(entityId, out var set) ? set : Array.Empty<ISession>();
}
