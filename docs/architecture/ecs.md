# ECS Architecture

## 라이브러리: Arch 2.1.0 + Arch.System 1.1.0

게임 서버의 ECS 레이어는 **Arch** 라이브러리를 기반으로 한다.  
이전에는 DefaultEcs 0.17.2를 사용했으나, 아키타입 기반 메모리 레이아웃과 더 활발한 유지보수를 위해 Arch로 교체했다.

> DefaultEcs: Sparse Set 방식 — 컴포넌트 타입별로 배열 분리, 엔티티 ID로 인덱싱  
> Arch: Archetype 방식 — 동일 컴포넌트 조합을 가진 엔티티를 연속 메모리에 묶어 저장, 벌크 조회 캐시 효율 높음

---

## 핵심 API 비교

| 작업 | DefaultEcs | Arch 2.1.0 |
|---|---|---|
| World 생성 | `new World()` | `World.Create()` (static factory) |
| World 파괴 | `world.Dispose()` | `World.Destroy(world)` (static) |
| 엔티티 생성 | `world.CreateEntity(); e.Set(comp)` | `world.Create(comp1, comp2, ...)` |
| 엔티티 파괴 | `entity.Dispose()` | `World.Destroy(entity)` |
| 생존 확인 | `entity.IsAlive` | `entity.IsAlive()` (extension method) |
| 컴포넌트 추가 (최초) | `entity.Set<T>(value)` (추가/갱신 모두) | `entity.Add(value)` |
| 컴포넌트 갱신 (기존) | `entity.Set<T>(value)` | `entity.Set(value)` |
| 컴포넌트 읽기 (복사) | `entity.Get<T>()` | `entity.Get<T>()` (Extensions) |
| 컴포넌트 참조 (수정) | `ref entity.Get<T>()` | `entity.TryGetRef<T>(out bool)` |
| 추가 또는 참조 | (직접 지원 없음) | `entity.AddOrGet<T>(default)` → `ref T` |
| 컴포넌트 제거 | `entity.Remove<T>()` | `entity.Remove<T>()` |
| 컴포넌트 존재 확인 | `entity.Has<T>()` | `entity.Has<T>()` (Extensions) |
| 엔티티 순회 | `EntitySet + AEntitySetSystem` | `World.Query(QueryDescription, lambda)` |
| 엔티티 수 세기 | `set.Count` | `World.CountEntities(in QueryDescription)` |
| 시스템 기반 클래스 | `AEntitySetSystem<float>` | `BaseSystem<World, float>` |
| 시스템 그룹 | `SequentialSystem<float>` | `Group<float>(name, systems...)` |
| 시스템 업데이트 시그니처 | `override void Update(float state)` | `override void Update(in float state)` |

```csharp
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
```

---

## QueryDescription

```csharp
// 컴포넌트 조합 필터
private readonly QueryDescription _query = new QueryDescription()
    .WithAll<PositionComponent, VelocityComponent, DestinationComponent>();

// 쿼리 실행 — 람다에서 ref로 컴포넌트를 직접 수정
World.Query(in _query, (Entity entity, ref PositionComponent pos, ref VelocityComponent vel) =>
{
    pos.Position = ...;  // 직접 수정됨 (ref)
});

// 엔티티 수 세기
int count = World.CountEntities(in _query);
```

### Entity 매개변수

람다에서 `Entity` 자체가 필요하면 첫 번째 파라미터로 추가한다. 불필요하면 생략 가능.

```csharp
// Entity 포함
World.Query(in _query, (Entity entity, ref PositionComponent pos) => { ... });

// Entity 생략 (컴포넌트만 접근)
World.Query(in _query, (ref PositionComponent pos, ref VelocityComponent vel) => { ... });
```

---

## 핵심 패턴

### 1. 구조적 변경 지연 (Structural Change Deferral)

쿼리 람다 내부에서 `Add<T>` / `Remove<T>`를 직접 호출하면 Arch 내부 배열이 재배치되어 반복이 깨진다.  
**반드시 쿼리를 완료한 뒤** 구조적 변경을 적용해야 한다.

```csharp
private readonly List<(Entity, bool)> _processed = new();

public override void Update(in float state)
{
    float dt = state; // in 파라미터는 람다 캡처 불가 → 로컬 복사 필수
    _processed.Clear();

    World.Query(in _query, (Entity entity, ref PositionComponent pos) =>
    {
        bool arrived = ...; // 계산만
        _processed.Add((entity, arrived));
    });

    // 쿼리 완료 후 구조적 변경 적용
    foreach (var (entity, arrived) in _processed)
    {
        if (!entity.IsAlive()) continue;
        if (arrived)
        {
            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();
        }
        ref var dirty = ref entity.AddOrGet<DirtyComponent>(default);
        dirty.PositionChanged = true;
    }
}
```

### 2. in 파라미터와 람다 캡처

`BaseSystem<World, float>`의 `Update(in float state)`에서 `state`는 `in` 파라미터라 람다 내부에서 직접 사용할 수 없다.

```csharp
public override void Update(in float state)
{
    float dt = state; // 반드시 로컬 변수로 복사

    World.Query(in _query, (ref VelocityComponent vel) =>
    {
        vel.Speed *= dt; // OK — 로컬 dt를 캡처
    });
}
```

### 3. 엔티티 딕셔너리 사전 구축 (Pre-built Entity Cache)

쿼리 람다 안에서 다른 엔티티를 찾기 위해 중첩 `World.Query`를 호출하면 주소 혼선(aliasing)이 발생할 수 있다.  
대신 Update 시작 시 `Dictionary<long, Entity>` 를 구축하고 O(1)로 조회한다.

```csharp
private readonly Dictionary<long, Entity> _entityById = new();

public override void Update(in float state)
{
    // Step 1: 전체 엔티티 캐시
    _entityById.Clear();
    World.Query(in _allQuery, (Entity entity, ref EntityIdComponent eid) =>
    {
        _entityById[eid.EntityId] = entity;
    });

    // Step 2: 메인 로직 — 딕셔너리로 O(1) 조회
    World.Query(in _attackerQuery, (ref AttackComponent atk) =>
    {
        if (!_entityById.TryGetValue(atk.TargetId, out var target)) return;
        // target 처리
    });
}
```

### 4. 엔티티 외부 수정 (TryGetRef)

쿼리 람다 밖에서 특정 엔티티의 컴포넌트를 수정할 때는 `TryGetRef`로 참조를 얻는다.

```csharp
ref var hp = ref target.TryGetRef<HealthComponent>(out bool exists);
if (!exists) return;
hp.Current -= damage; // 직접 수정 — 아키타입 메모리를 직접 가리킴
```

### 5. 컴포넌트 추가 또는 참조 (AddOrGet)

컴포넌트가 있을 수도 없을 수도 있을 때, 없으면 기본값으로 추가하고 참조를 반환한다.

```csharp
ref var dirty = ref entity.AddOrGet<DirtyComponent>(default);
dirty.PositionChanged = true;
```

---

## 시스템 구조

### BaseSystem 상속

```csharp
public class MovementSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<PositionComponent, VelocityComponent, DestinationComponent>();

    public MovementSystem(World world) : base(world) { }

    public override void Update(in float state)
    {
        float dt = state;
        // ...
    }
}
```

### 시스템 그룹 (Group)

Zone에서 여러 시스템을 순서대로 실행:

```csharp
protected override ISystem<float> CreateSystems()
{
    return new Group<float>($"Zone-{ZoneId}",
        new MovementSystem(World),
        new AoiSystem(World, AoiGrid, SubscriberMap),
        new BroadcastSystem(World, SubscriberMap),
        new SessionSystem(World, AoiGrid)
    );
}

// 게임루프에서 호출
Systems.Update(FixedDeltaTime); // float literal은 in float에 직접 전달 가능
```

---

## Zone 스레드 모델과 ECS

- **게임루프 스레드 (20Hz)**: ECS 시스템 실행, `_pendingActions` 큐 소비
- **네트워크 스레드**: 패킷 수신 → `EnqueueAction(Action)` 호출

```csharp
// 네트워크 스레드에서 호출 (thread-safe)
public void HandleMove(long entityId, Vector3 destination, float speed)
{
    EnqueueAction(() =>
    {
        // 이 람다는 게임루프 스레드에서 실행됨
        World.Query(in _allEntitiesQuery, (Entity entity, ref EntityIdComponent id) =>
        {
            if (id.EntityId != entityId) return;
            if (entity.Has<DestinationComponent>()) entity.Set(new DestinationComponent(destination));
            else entity.Add(new DestinationComponent(destination));
        });
    });
}
```

**규칙**: 네트워크 스레드에서 ECS 세계를 직접 수정하면 안 된다. 반드시 `EnqueueAction`을 거쳐야 한다.

---

## WorldObject는 ECS 외부

채집/상호작용 오브젝트(`WorldObject`)는 Zone 내 `List<WorldObject>`로 관리한다.  
정적 오브젝트이고 상태 변경이 드물기 때문에 ECS 벌크 반복의 이점이 없다.  
몬스터처럼 매 틱 위치/전투를 갱신해야 하는 동적 엔티티만 ECS Entity로 관리한다.

---

## 현재 시스템 목록

| 시스템 | 역할 | 실행 순서 |
|---|---|---|
| `MovementSystem` | DestinationComponent 향해 이동, 도착 시 Dest/Vel 제거, DirtyComponent 설정 | 1 |
| `AoiSystem` | 그리드 동기화, 시야 진입/이탈 감지, S2C_Spawn/Despawn 전송 | 2 |
| `BroadcastSystem` | DirtyComponent 보유 엔티티의 위치를 SubscriberMap 기반으로 S2C_Move 브로드캐스트 | 3 |
| `SessionSystem` | 연결 끊긴 세션 엔티티 제거 | 4 |
| `CombatSystem` | 몬스터 자동 공격 처리 (DungeonZone에서만 사용) | DungeonZone 전용 |
| `MonsterAISystem` | 몬스터 타겟 탐색 및 추적 이동 | DungeonZone 전용 |
