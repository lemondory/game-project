# MMO Game Server & Client

실시간 멀티플레이어 온라인 게임의 서버-클라이언트 프로젝트.
ECS 기반 게임 서버(.NET 9)와 Unity 클라이언트로 구성된다.

---

## 기술 스택

| 영역 | 기술 |
|---|---|
| **Server** | .NET 9.0, DefaultEcs, System.IO.Pipelines, Protobuf |
| **Database** | PostgreSQL, Dapper, 3-DB 분리 (auth / common / game) |
| **Client** | Unity, New Input System, Protobuf |
| **Protocol** | 커스텀 TCP `[Size 2B LE][PacketId 2B LE][Protobuf]` |
| **Data** | Excel → MessagePack 변환 (DataConverter), 서버/클라이언트 공유 |
| **Infra** | Docker Compose (PostgreSQL) |

---

## 아키텍처

### 서버 네트워크 레이어

```
Socket.ReceiveAsync
      │
   Pipe.Writer          ← System.IO.Pipelines (zero-copy 버퍼링)
      │
   Pipe.Reader (ParseLoopAsync)
      │   프레임 파싱: [Size][PacketId][Body]
      │   · 최소/최대 크기 검증
      │
   PacketHandler         ← C2S 패킷 라우팅

Send(packet)
      │
   Channel<byte[]>       ← bounded 4096, backpressure 지원
      │
   SendLoopAsync → Socket.SendAsync
```

### 게임 루프 (Zone)

각 Zone은 독립적인 게임루프 스레드(20Hz)에서 ECS 시스템을 실행한다.
네트워크 스레드의 요청은 `ConcurrentQueue<Action>`을 통해 게임루프 스레드에서 안전하게 처리된다.

```
Zone (20Hz fixed tick)
  ├─ ConcurrentQueue<Action>  ← 네트워크 → 게임루프 스레드 안전 전달
  ├─ MovementSystem           ← 목적지까지 이동 처리
  ├─ AoiSystem                ← 공간 해시 기반 관심 영역(AOI) 관리
  ├─ BroadcastSystem          ← 변경된 엔티티 위치를 S2C_Move로 전파
  └─ SessionSystem            ← 연결 해제 엔티티 제거 + S2C_Despawn 전파

DungeonZone (Zone 확장)
  ├─ 위 시스템 전체
  ├─ MonsterAISystem          ← 몬스터 탐지/추적/공격 AI
  └─ CombatSystem             ← 자동 공격 타이머, 데미지 계산, 사망 처리
```

### AOI (Area of Interest)

```
AoiGrid (공간 해시 그리드, 셀 크기 = ViewRadius)
  │
AoiSystem (매 틱 실행)
  ├─ 엔티티 위치 변경 시 그리드 셀 업데이트
  ├─ 현재 셀 + 인접 8셀의 엔티티를 관심 목록으로 수집
  ├─ 새로 진입한 엔티티 → S2C_Spawn 전송
  └─ 벗어난 엔티티 → S2C_Despawn 전송
```

### 클라이언트 흐름

```
Login Scene
  │  C2S_Login → S2C_LoginResult
  │
  ▼
Town Scene
  │  S2C_EnterTownResult (NearbyEntities 포함)
  │  TownManager: 플레이어/엔티티 스폰, 이동, 채팅
  │  DungeonPortal: 거리 기반 포탈 감지 → DungeonEntryPopup
  │
  ▼  C2S_EnterDungeon → S2C_EnterDungeonResult
Dungeon Scene
     DungeonManager: 몬스터 스폰, 전투 패킷 처리 (Attack/Damage/Death)
     ESC → C2S_EnterTown → 마을 복귀
```

### 클라이언트 네트워크 (NetworkManager)

```
네트워크 스레드: OnReceive → AppendToPacketBuffer → ProcessPackets → EnqueueMainThread
메인 스레드:     Update() → 큐 drain → HandlePacket → [PacketHandler] 핸들러 → 이벤트

패킷 핸들러 자동 등록:
  [PacketHandler(PacketId.X)] 어트리뷰트 → Awake() 시 리플렉션으로 Dictionary에 등록
  핸들러 구현은 partial class로 컨텐츠별 파일 분리 (Handlers/ 폴더)
```

---

## 성능 테스트 결과

로컬 환경(MacBook, Apple Silicon)에서 20개 봇 클라이언트 동시 접속 후 약 83분 지속 실행한 결과.

| 항목 | 결과 |
|---|---|
| 동시 접속 | 20 클라이언트 (이동 + 채팅 반복) |
| 틱 평균 처리 시간 | **~1.0ms** (예산 50ms 대비 2%) |
| 틱 최대 처리 시간 | 335ms (GC 스파이크, 단발성) |
| S2C_Move 총 전송량 | 약 3,160만 패킷 (83분) |
| 초당 S2C_Move | 약 **6,350 패킷/초** |
| 에러/크래시 | **0건** |

> `dotnet run -- bot 20` 으로 재현 가능 (bot01~bot20 계정 필요 — `db/scripts/create_test_bots.sql`)

---

## 주요 구현 포인트

- **System.IO.Pipelines**: 수신 버퍼를 `Pipe`로 관리해 TCP 스트림 단편화를 zero-copy로 처리
- **Channel\<T\>**: bounded send queue로 느린 클라이언트에 대한 backpressure 적용
- **ECS (DefaultEcs)**: 컴포넌트 기반 엔티티 설계, 시스템별 단일 책임
- **스레드 안전**: 네트워크 스레드의 엔티티 변경을 `ConcurrentQueue<Action>`으로 게임루프 스레드에 위임
- **AOI**: 공간 해시 그리드로 O(1) 셀 조회, 관심 영역 변경 시에만 Spawn/Despawn 전송
- **전투 시스템**: 데미지 공식 `max(1, AttackPower - Defense)`, 몬스터 AI (탐지→추적→공격 FSM)
- **입력 검증**: 패킷 크기 범위, 채팅 길이(200자), 이동 좌표 범위(±10000), 포탈 거리 서버 검증
- **데이터 파이프라인**: Excel → DataConverter → MessagePack(.bytes) + C# 코드 자동 생성
- **클라이언트 핸들러 자동 등록**: `[PacketHandler]` 어트리뷰트 + 리플렉션, IL2CPP link.xml로 안전 보장

---

## 프로젝트 구조

```
game-project/
├── server/
│   ├── src/
│   │   ├── GameServer/
│   │   │   ├── Network/         # Session (Pipelines+Channel), TcpServer, PacketHandler
│   │   │   ├── Game/
│   │   │   │   ├── Components/  # ECS 컴포넌트 (Position, Health, AI, Interest, ...)
│   │   │   │   ├── Systems/     # Movement, AOI, Broadcast, Session, Combat, MonsterAI
│   │   │   │   └── Zones/       # Zone(base), TownZone, DungeonZone, ZoneManager
│   │   │   └── Database/        # Dapper, 3-DB (auth/common/game)
│   │   ├── GameShared/          # 서버-클라이언트 공유
│   │   │   ├── Protos/          # .proto 정의 (login, common, combat)
│   │   │   ├── Enums/           # PacketId, ZoneType
│   │   │   ├── Generated/       # DataConverter 자동 생성 코드
│   │   │   └── Data/            # DataManagerBase, IDataTable
│   │   └── GameClient/          # 테스트 봇 (Bot, DungeonBot)
│   ├── data/                    # xlsx / csv / bytes 게임 데이터
│   ├── db/                      # PostgreSQL 초기화 SQL
│   ├── tools/DataConverter/     # Excel → MessagePack 변환 도구
│   └── docker-compose.yml
└── client/
    └── Assets/_Project/
        ├── Scripts/
        │   ├── Network/         # NetworkManager (코어)
        │   │   └── Handlers/    # partial class 핸들러 (Login/Zone/Entity/Chat/Combat)
        │   ├── Game/            # TownManager, DungeonManager, PlayerController, DungeonPortal
        │   └── UI/              # LoginUI, ChatUI, DungeonEntryPopup
        ├── Scenes/              # Login, Town, Dungeon
        ├── StreamingAssets/     # MessagePack 데이터 (.bytes)
        └── Plugins/             # GameShared.dll, Google.Protobuf.dll
```

---

## 실행 방법

### 1. DB 실행

```bash
cd server
docker-compose up -d
```

### 2. 서버 실행

```bash
cd server/src/GameServer
dotnet run
```

### 3. 봇 클라이언트

```bash
# 숫자 지정 (자동 계정)
cd server/src/GameClient
dotnet run -- bot 3

# 특정 계정 지정
dotnet run -- bot alice bob

# 던전 봇
dotnet run -- dungeon 1 testuser1
```

### 4. Unity 클라이언트

Unity Editor에서 `client/` 폴더를 열고 Login 씬을 실행한다.

### 환경 변수 (선택)

DB 연결 문자열은 환경변수로 오버라이드 가능하다. `.env.example` 참고.

---

## 패킷 목록

| 방향 | PacketId | 설명 |
|---|---|---|
| **Login** | | |
| C→S | `C2S_Login` (1000) | 로그인 요청 |
| S→C | `S2C_LoginResult` (1001) | 로그인 결과 |
| S→C | `S2C_ForceLogout` (1002) | 강제 로그아웃 |
| **Zone** | | |
| C→S | `C2S_EnterTown` (1100) | 마을 입장 요청 |
| S→C | `S2C_EnterTownResult` (1101) | 마을 입장 결과 (NearbyEntities 포함) |
| C→S | `C2S_EnterDungeon` (1110) | 던전 입장 요청 |
| S→C | `S2C_EnterDungeonResult` (1111) | 던전 입장 결과 |
| **Movement** | | |
| C→S | `C2S_Move` (2000) | 이동 목적지 전송 |
| S→C | `S2C_Move` (2001) | 엔티티 현재 위치 브로드캐스트 |
| S→C | `S2C_Spawn` (2010) | 엔티티 생성 (AOI 진입) |
| S→C | `S2C_Despawn` (2011) | 엔티티 제거 (AOI 이탈/연결 해제) |
| **Chat** | | |
| C→S | `C2S_Chat` (3000) | 채팅 전송 |
| S→C | `S2C_Chat` (3001) | 채팅 브로드캐스트 |
| **Combat** | | |
| C→S | `C2S_Attack` (4000) | 공격 요청 |
| S→C | `S2C_Attack` (4001) | 공격 애니메이션 알림 |
| S→C | `S2C_Damage` (4002) | 데미지 결과 (현재/최대 HP 포함) |
| S→C | `S2C_Death` (4003) | 사망 알림 |
| S→C | `S2C_RewardResult` (4004) | 경험치/골드 보상 |
| S→C | `S2C_LevelUp` (4005) | 레벨업 알림 |

---

## 구현 완료

- [x] TCP 네트워크 레이어 (System.IO.Pipelines + Channel, backpressure 지원)
- [x] 로그인/인증 (PostgreSQL auth DB, 중복 로그인 강제 해제)
- [x] 마을 존 (이동, 채팅, 엔티티 스폰/디스폰)
- [x] AOI 시스템 (공간 해시 그리드, 관심 영역 기반 브로드캐스트)
- [x] 던전 존 (몬스터 스폰, 전투, 보상, 레벨업)
- [x] 몬스터 AI (탐지 → 추적 → 공격 FSM, 사망 플레이어 타깃 제외)
- [x] 게임 데이터 파이프라인 (Excel → MessagePack → C# 자동 생성)
- [x] 씬 전환 로딩 화면 (LoadSceneAsync + 프로그레스 바)
- [x] 전투 클라이언트 UI (HP 바, 타깃 인디케이터, 사망/리스폰 패널)
- [x] 테스트 봇 클라이언트 (마을 봇, 던전 봇)
- [x] 클라이언트 패킷 핸들러 자동 등록 (어트리뷰트 + partial class, IL2CPP 호환)
- [x] 스트레스 테스트 검증 (20 클라이언트, 83분, ~6,350 패킷/초, 에러 0건)

## 구현 예정

- [ ] 채팅 UI 완성
- [ ] 하트비트 / 좀비 연결 감지
- [ ] 클라이언트별 rate limiting
- [ ] 던전 서버 분리 (토큰 기반 리다이렉트)
