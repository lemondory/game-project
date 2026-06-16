# 전투 시스템 설계

## 설계 원칙

- **서버 권위적(Server-Authoritative)**: 모든 전투 결과는 서버가 최종 결정
- **클라이언트 선처리(Client-Side Prediction)**: 입력 즉시 UI 반응, 서버 결과로 보정
- **공식 공유(Shared Formula)**: `GameShared.Combat.CombatCalculator`에 계산 로직 배치 → 클라·서버 동일 공식 사용
- **공격 즉시 처리**: 전투 패킷은 게임루프 틱을 기다리지 않고 수신 즉시 계산·응답 (이동/AI는 틱에서 처리)

---

## 전투 흐름

```
[클라이언트]                          [서버]
     │
     │  공격 버튼 입력
     │  ┌──────────────────────────┐
     │  │ 1. 사거리/쿨타임 로컬 체크    │
     │  │ 2. 예측 데미지 계산          │  ← GameShared.CombatCalculator 사용
     │  │ 3. 데미지 숫자 UI 즉시 표시   │
     │  │ 4. C2S_Attack 패킷 전송      │
     │  └──────────────────────────┘
     │──────────────────────────────────────→
     │                                     │
     │                          수신 즉시 처리 (틱 밖)
     │                          ┌─────────────────────┐
     │                          │ 1. 사거리/쿨타임 검증  │
     │                          │ 2. 데미지 계산        │  ← 동일 공식
     │                          │ 3. S2C_AttackResult   │  → 공격자에게
     │                          │ 4. S2C_Damage         │  → 주변 플레이어에게
     │                          └─────────────────────┘
     │                          HP 반영은 다음 틱에 ECS 적용
     │
     │←─────────────────────────────────────
     │  S2C_AttackResult 수신
     │  ┌─────────────────────────┐
     │  │ success=true  → HP바 보정 (조용히) │
     │  │ success=false → 데미지 UI 롤백     │
     │  └─────────────────────────┘
```

---

## GameShared 공유 범위

### `GameShared/Combat/CombatCalculator.cs` (신규)

클라·서버 모두 동일하게 사용하는 순수 계산 함수 모음.

```csharp
public static class CombatCalculator
{
    // 기본 데미지 공식
    public static int CalculateDamage(int attackPower, int defense)
        => Math.Max(1, attackPower - defense);

    // 스킬 데미지 공식
    public static int CalculateSkillDamage(int attackPower, int defense, int skillDamage)
        => Math.Max(1, (attackPower + skillDamage) - defense);

    // 사거리 체크
    public static bool IsInRange(float x1, float z1, float x2, float z2, float range)
    {
        float dx = x1 - x2, dz = z1 - z2;
        return dx * dx + dz * dz <= range * range;
    }
}
```

### GameShared에 두는 것 / 두지 않는 것

| 항목 | GameShared | 이유 |
|---|---|---|
| 데미지 공식 | O | 클라 예측과 서버 계산이 동일해야 함 |
| 사거리 공식 | O | 클라 로컬 체크에서도 사용 |
| SkillData (쿨타임·데미지값) | O (이미 있음) | 양쪽에서 참조 |
| ECS 컴포넌트 | X | Unity에서 Arch 사용 불가 |
| 쿨타임 상태 추적 | X | 클라·서버 각자 독립 관리 |
| 브로드캐스트 로직 | X | 서버 전용 |

---

## 패킷 설계

### 기존 proto (combat.proto) 검토

| 패킷 | 방향 | 상태 | 비고 |
|---|---|---|---|
| `C2S_Attack` | 클라→서버 | **수정 필요** | skillId 추가 |
| `S2C_Attack` | 서버→클라 | **용도 변경** | 브로드캐스트용 공격 모션 알림 |
| `S2C_Damage` | 서버→클라 | 유지 | 피격 대상 HP 변경 브로드캐스트 |
| `S2C_Death` | 서버→클라 | 유지 | 대상 사망 브로드캐스트 |
| `S2C_RewardResult` | 서버→클라 | 유지 | 킬 보상 (공격자 전용) |
| `S2C_LevelUp` | 서버→클라 | 유지 | 레벨업 (공격자 전용) |

### 추가 패킷

```protobuf
// C2S_Attack 수정 — 스킬 포함
message C2S_Attack {
  int64 target_entity_id = 1;
  int32 skill_id = 2;          // 0 = 기본 공격
}

// 공격자에게만 전송 — 성공/실패 확인용
message S2C_AttackResult {
  bool  success         = 1;
  int32 damage          = 2;   // 실제 데미지 (클라 예측값 보정용)
  int64 target_entity_id = 3;
  string fail_reason    = 4;   // "out_of_range" | "on_cooldown" | "target_dead"
}
```

### 패킷 수신자 구분

```
공격 발생 시 서버가 전송하는 패킷:

공격자 본인  ← S2C_AttackResult (성공/실패, 실제 데미지)
피격 대상   ← S2C_Damage (현재HP)
주변 전체   ← S2C_Attack (공격 모션 재생용), S2C_Damage (HP바 갱신)
피격 사망   ← S2C_Death (브로드캐스트)
공격자 본인  ← S2C_RewardResult, S2C_LevelUp (킬 시)
```

---

## 서버 검증 항목

공격 패킷 수신 시 서버가 체크하는 순서:

1. **대상 존재** — target entity가 살아있는지
2. **사거리** — 공격자와 대상 거리 ≤ 공격 범위
3. **쿨타임** — 마지막 공격 이후 쿨타임 경과 여부
4. **스킬 유효성** — skillId가 해당 클래스가 사용 가능한 스킬인지
5. **같은 팀 여부** — (추후) PvP 구현 시 아군 공격 방지

---

## 쿨타임 관리

- **서버**: ECS `AttackComponent.LastAttackTime` — 권위적 쿨타임 기준
- **클라이언트**: Unity 내 로컬 타이머 — UI 비활성화 + 로컬 체크용

서버 쿨타임이 기준이므로, 클라가 쿨타임 미경과 상태로 패킷을 보내면 서버가 거부하고 `S2C_AttackResult { success=false, fail_reason="on_cooldown" }` 반환.

---

## 구현 순서

### Phase 1 — 기본 공격 완성
1. `GameShared/Combat/CombatCalculator.cs` 생성 (공식 분리)
2. `combat.proto` 수정 — `C2S_Attack`에 skillId 추가, `S2C_AttackResult` 추가
3. 서버 `HandleAttack` 리팩터 — 즉시 처리 경로 + `S2C_AttackResult` 전송
4. 클라이언트 — 공격 입력 시 예측 데미지 표시 + 패킷 전송 + 결과 수신 시 보정

### Phase 2 — 스킬 시스템
1. `SkillData` 확장 — 범위(AoE), 타입(근거리/원거리/범위), 투사체 여부
2. 스킬별 계산 분기 (`CombatCalculator.CalculateSkillDamage`)
3. 클라 스킬 UI — 쿨타임 표시, 스킬 이펙트

### Phase 3 — 상태이상 (추후)
- 스턴, 슬로우, 독 등 DoT 효과
- `StatusEffectComponent` ECS 추가
- 서버 틱에서 DoT 처리 (즉시 처리 대상 아님)

---

## 현재 데이터 구조 현황

### SkillData (xlsx → GameShared)
- `SkillId`, `Name`, `Type (SkillType)`, `Cooldown`, `Damage[]` (레벨별)

### MonsterData
- `AttackPower`, `Defense`, `AttackRange`, `AttackCooldown`, `SkillIds[]`

### CharacterClassData
- `BaseHp/Attack/Defense`, `HpPerLevel/AttackPerLevel/DefensePerLevel`

### 현재 데미지 공식 (서버 하드코딩 → CombatCalculator로 이동 예정)
```csharp
int damage = Math.Max(1, attacker.AttackPower - target.Defense);
```
