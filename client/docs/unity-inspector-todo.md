# Unity Inspector 작업 목록

코드 작업은 완료되었으나 Unity 에디터에서 Inspector 연결이 필요한 항목들입니다.
씬/프리팹을 열어서 아래 순서대로 작업하면 됩니다.

---

## Field 씬

### FieldHUD (FieldHUD.cs)

`Field` 씬 → Hierarchy → FieldHUD GameObject 선택

**사망 UI — deathPanel 구성 (신규)**

deathPanel 안에 버튼 2개를 추가해야 합니다.

1. Button 추가: 텍스트 "입구에서 부활"
   - FieldHUD Inspector → `Respawn Button` 필드에 이 버튼 연결

2. Button 추가: 텍스트 "마을로 이동"
   - FieldHUD Inspector → `Return To Town Button` 필드에 이 버튼 연결

> 기존 `deathPanel`이 있다면 그 안에 버튼 2개를 자식으로 추가.
> deathPanel 자체가 없다면 Panel UI를 새로 만들고 `Death Panel` 필드에 연결.

---

### FieldEntryPopup (FieldEntryPopup.cs)

`Field` 씬 또는 프리팹 → FieldEntryPopup GameObject 선택

| Inspector 필드 | 연결 대상 |
|---|---|
| `Prompt Text` | "[E] 사냥터이름 입장" 텍스트 오브젝트 |

> 이게 연결 안 되어 있으면 포탈 근처에서 "[E] Enter" 대신 아무것도 표시 안 됨.

---

---

## Field 씬 — 채집 오브젝트 프리팹

현재 채집 오브젝트는 런타임에 구체 프리미티브로 생성됩니다.
나중에 실제 프리팹으로 교체할 때 아래 작업이 필요합니다.

### 약초 프리팹 (`Collectible/Herb`)

1. `Assets/_Project/Prefabs/Collectibles/` 폴더 생성
2. 약초 모델(또는 임시 구체) 프리팹 생성 → `FieldCollectible.cs` 컴포넌트 부착
3. `FieldManager.cs`의 `OnObjectInfo()`에서 `PrefabKey`로 프리팹 로드하도록 교체

### 철광석 프리팹 (`Collectible/Ore`)

1. 광석 모델(또는 임시 큐브) 프리팹 생성 → `FieldCollectible.cs` 컴포넌트 부착

> 프리팹 교체 전까지는 초록 구체(채집 가능) / 회색 구체(채집됨)로 표시됩니다.

---

## 확인 항목

작업 후 플레이 모드에서 테스트:

- [ ] 필드 입장 → 포탈 근처에서 "[E] 슬라임 평원 입장" 프롬프트 표시
- [ ] 몬스터에게 사망 → deathPanel 표시 (버튼 2개)
- [ ] "입구에서 부활" 클릭 → 입구로 텔레포트, HP 회복, 패널 닫힘
- [ ] "마을로 이동" 클릭 → 마을 씬 전환
