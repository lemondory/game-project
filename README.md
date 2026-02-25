# MMO Game Server & Client Prototype

실시간 멀티플레이어 게임의 서버-클라이언트 프로토타입.
ECS 기반 게임 서버(.NET 9)와 Unity 클라이언트로 구성된다.

---

## 기술 스택

| 영역 | 기술 |
|---|---|
| **Server** | .NET 9.0, DefaultEcs, System.IO.Pipelines, Protobuf |
| **Database** | PostgreSQL, Dapper, 3-DB 분리 (auth / common / game) |
| **Client** | Unity, New Input System, Protobuf |
| **Protocol** | 커스텀 TCP `[Size 2B][PacketId 2B][Protobuf]` |
| **Infra** | Docker Compose |

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
      │   · 최소 크기 / 최대 크기 검증
      │
   PacketQueue (ConcurrentQueue)
      │
   PacketHandler        ← 메인 루프에서 처리
```

```
Send(packet)
      │
   Channel<byte[]>      ← bounded 4096, backpressure 지원
      │
   SendLoopAsync → Socket.SendAsync
```

### 게임 루프 (Zone)

```
Zone (20Hz fixed tick)
  ├─ ConcurrentQueue<Action>  ← 네트워크 스레드 → 게임루프 스레드 안전 전달
  ├─ MovementSystem           ← 목적지까지 이동 처리
  ├─ BroadcastSystem          ← 변경된 엔티티 위치를 S2C_Move 로 전파
  └─ SessionSystem            ← 연결 해제된 엔티티 제거 + S2C_Despawn 전파
```

### 클라이언트 흐름

```
Login Scene ──S2C_LoginResult──▶ C2S_EnterTown
                                      │
                             S2C_EnterTownResult
                              (NearbyEntities 포함)
                                      │
                             SceneManager.LoadScene("Town")
                                      │
                             TownManager.Initialize()
                              · MyPlayer 스폰
                              · OtherPlayer 스폰
```

---

## 주요 구현 포인트

- **System.IO.Pipelines**: 수신 버퍼를 `Pipe`로 관리해 TCP 스트림 단편화를 제로카피로 처리
- **Channel\<T\>**: bounded send queue로 느린 클라이언트에 대한 backpressure 적용
- **ECS (DefaultEcs)**: 컴포넌트 기반 엔티티 설계, 시스템별 단일 책임
- **스레드 안전**: 네트워크 스레드의 엔티티 변경을 `ConcurrentQueue<Action>`으로 게임루프 스레드에 위임
- **입력 검증**: 패킷 크기 범위, 채팅 길이(200자), 이동 좌표 범위(±10000) 서버 검증
- **Unity 메인 스레드 큐**: 네트워크 콜백을 `lock`으로 보호된 큐에 넣고 `Update()`에서 처리

---

## 프로젝트 구조

```
game-project/
├── server/
│   ├── src/
│   │   ├── GameServer/          # 서버 메인
│   │   │   ├── Network/         # Session, TcpServer, PacketHandler
│   │   │   ├── Game/
│   │   │   │   ├── Components/  # ECS 컴포넌트
│   │   │   │   ├── Systems/     # MovementSystem, BroadcastSystem, ...
│   │   │   │   └── Zones/       # TownZone, DungeonZone
│   │   │   └── Database/        # Repository 패턴, 3-DB
│   │   ├── GameShared/          # 서버-클라이언트 공유 (Protobuf, Enum)
│   │   └── GameClient/          # 테스트용 봇 클라이언트
│   ├── db/init/                 # PostgreSQL 초기화 SQL
│   ├── tools/DataConverter/     # 게임 데이터 변환 도구
│   └── docker-compose.yml
└── client/
    └── Assets/_Project/
        ├── Scripts/
        │   ├── Network/         # NetworkManager (TCP, 메인스레드 큐)
        │   ├── Game/            # TownManager, PlayerController, EntityMover
        │   └── UI/              # LoginUI, ChatUI
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

### 3. 봇 클라이언트 (멀티플레이어 테스트)

```bash
cd server/src/GameClient
dotnet run -- bot 3
```

### 4. Unity 클라이언트

Unity Editor에서 `client/` 폴더를 열고 Login 씬을 실행한다.

### 환경 변수 (선택)

DB 연결 문자열은 환경변수로 오버라이드 가능하다. `.env.example` 참고.

---

## 패킷 목록

| 방향 | PacketId | 설명 |
|---|---|---|
| C→S | `C2S_Login` | 로그인 요청 |
| S→C | `S2C_LoginResult` | 로그인 결과 |
| C→S | `C2S_EnterTown` | 마을 입장 요청 |
| S→C | `S2C_EnterTownResult` | 마을 입장 결과 (NearbyEntities 포함) |
| S→C | `S2C_Spawn` | 엔티티 생성 알림 |
| S→C | `S2C_Despawn` | 엔티티 제거 알림 |
| C→S | `C2S_Move` | 이동 목적지 전송 |
| S→C | `S2C_Move` | 엔티티 현재 위치 브로드캐스트 |
| C→S | `C2S_Chat` | 채팅 전송 |
| S→C | `S2C_Chat` | 채팅 브로드캐스트 |
