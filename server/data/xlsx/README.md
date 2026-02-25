# Sample XLSX Data Files

이 폴더에는 DataConverter가 처리할 XLSX 파일들을 배치합니다.

## 파일 구조

### 1. enums.xlsx (필수)

여러 시트로 구성되며, 각 시트는 하나의 Enum을 정의합니다.

**Sheet: MonsterType**
```
| Name      | Value | Description    |
|-----------|-------|----------------|
| string    | int   | string         |
| Enum 이름  | 값    | 설명           |
| Normal    | 0     | 일반 몬스터     |
| Elite     | 1     | 엘리트 몬스터   |
| Boss      | 2     | 보스 몬스터     |
```

**Sheet: ItemRarity**
```
| Name      | Value | Description |
|-----------|-------|-------------|
| string    | int   | string      |
| Common    | 0     | 일반        |
| Rare      | 1     | 레어        |
| Epic      | 2     | 에픽        |
| Legendary | 3     | 전설        |
```

### 2. MonsterData.xlsx (예시)

```
| MonsterId | Name      | Type        | Level | Hp   | AttackPower | MoveSpeed | DropRate |
|-----------|-----------|-------------|-------|------|-------------|-----------|----------|
| int       | string    | MonsterType | int   | int  | int         | fixed     | float    |
| 몬스터ID   | 이름       | 타입        | 레벨  | 체력 | 공격력       | 이동속도   | 드롭확률  |
| 1001      | Slime     | Normal      | 1     | 100  | 10          | 1.5       | 0.5      |
| 1002      | Goblin    | Elite       | 5     | 300  | 25          | 2.0       | 0.3      |
| 1003      | Dragon    | Boss        | 50    | 5000 | 200         | 1.0       | 0.9      |
```

**fixed 타입**: Fixed32로 변환됩니다 (10000분율, 4자리 소수점)

### 3. ItemData.xlsx (예시)

```
| ItemId | Name          | Rarity     | Price | MaxStack | Description           |
|--------|---------------|------------|-------|----------|-----------------------|
| int    | string        | ItemRarity | int   | int      | string                |
| 2001   | Health Potion | Common     | 10    | 99       | Restores 50 HP        |
| 2002   | Mana Potion   | Common     | 15    | 99       | Restores 30 MP        |
| 2003   | Magic Sword   | Epic       | 1000  | 1        | ATK +50, Magic +20    |
| 2004   | Dragon Scale  | Legendary  | 5000  | 10       | Rare crafting material|
```

### 4. SkillData.xlsx (배열 예시)

**옵션 A: 다중 컬럼 배열**
```
| SkillId | Name      | Damage[] | Damage[] | Damage[] | ManaCost[] | ManaCost[] | ManaCost[] |
|---------|-----------|----------|----------|----------|------------|------------|------------|
| int     | string    | int      | int      | int      | int        | int        | int        |
| 101     | FireBall  | 100      | 150      | 200      | 10         | 15         | 20         |
| 102     | IceBolt   | 80       | 120      | 160      | 8          | 12         | 16         |
```

**옵션 B: 구분자 배열**
```
| SkillId | Name      | Damage   | ManaCost |
|---------|-----------|----------|----------|
| int     | string    | int[]    | int[]    |
| 101     | FireBall  | 100,150,200 | 10,15,20 |
| 102     | IceBolt   | 80,120,160  | 8,12,16  |
```

## 생성 방법

### Excel로 생성
1. Excel 또는 Google Sheets에서 위 구조대로 작성
2. .xlsx 형식으로 저장
3. `data/xlsx/` 폴더에 배치

### CSV에서 변환
```bash
# CSV 파일을 Excel에서 열기
# 다른 이름으로 저장 → .xlsx 선택
```

## DataConverter 실행

```bash
# 전체 변환
dotnet run --project tools/DataConverter/DataConverter -- \
  --input data/xlsx \
  --output-code src/GameShared/Generated/Data \
  --output-bytes data/bytes \
  --output-csv data/csv

# 단일 파일 변환
dotnet run --project tools/DataConverter/DataConverter -- \
  --file data/xlsx/MonsterData.xlsx

# 검증만 수행
dotnet run --project tools/DataConverter/DataConverter -- --validate-only
```

## 출력 결과

### C# 클래스
- `src/GameShared/Generated/Data/MonsterData.cs`
- `src/GameShared/Generated/Data/MonsterDataTable.cs`
- `src/GameShared/Generated/Enums/MonsterType.cs`

### MessagePack 바이너리
- `data/bytes/MonsterData.bytes`

### CSV (검증용)
- `data/csv/MonsterData.csv`

## 지원 데이터 타입

### 기본 타입
- `int`, `long`, `short`, `byte`
- `float`, `double`, `decimal`
- `fixed` (Fixed32, 10000분율)
- `string`
- `bool`
- `DateTime`

### 특수 타입
- `TypeName?` - Nullable 타입
- `TypeName[]` - 배열 (다중 컬럼 또는 구분자)
- `EnumName` - 사용자 정의 Enum (enums.xlsx 정의 필요)
- `ref:TableName` - 외래키 (검증 예정)

## 예제 파일

실제 XLSX 파일은 Excel, Google Sheets, LibreOffice Calc 등에서 작성해야 합니다.

테스트를 위해 최소한 다음 파일들이 필요합니다:
- `enums.xlsx` (MonsterType, ItemRarity 정의)
- `MonsterData.xlsx`
- `ItemData.xlsx`
