using System.Net.Sockets;
using GameShared.Enums;
using GameShared.Proto;
using Google.Protobuf;
using Serilog;

namespace GameClient;

/// <summary>
/// 던전 전투 테스트 봇
/// 로그인 → 마을 입장 → 던전 입장 → 몬스터 자동 공격 → 보상 수신
/// </summary>
public class DungeonBot
{
    private readonly string _username;
    private readonly string _password;
    private readonly int _dungeonId;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly List<byte> _buffer = new();
    private readonly byte[] _readBuf = new byte[4096];

    private long _entityId;
    private bool _inTown;
    private bool _inDungeon;
    private volatile bool _isKicked;

    // 던전 내 알려진 몬스터 엔티티 ID 목록 (thread-safe 접근)
    private readonly List<long> _monsterIds = new();
    private readonly object _monstersLock = new();

    // 플레이어 현재 스탯 (패킷으로 업데이트)
    private int _level = 1;
    private int _exp = 0;
    private int _gold = 0;
    private int _hp = 0;
    private int _maxHp = 0;

    public string Name => _username;

    public DungeonBot(string username, string password = "test", int dungeonId = 1)
    {
        _username = username;
        _password = password;
        _dungeonId = dungeonId;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync("127.0.0.1", 7777, ct);
            _stream = _client.GetStream();
            Log.Information("[DungeonBot:{Name}] Connected", _username);

            // Step 1: 로그인
            Send(PacketId.C2S_Login, new C2S_Login { Username = _username, Password = _password });

            var receiveTask = ReceiveLoopAsync(ct);

            // Step 2: 마을 입장 대기 (최대 5초)
            await WaitForCondition(() => _inTown, timeoutSec: 5, ct);
            if (!_inTown)
            {
                Log.Warning("[DungeonBot:{Name}] 마을 입장 타임아웃", _username);
                return;
            }

            // Step 3: 던전 입장
            Log.Information("[DungeonBot:{Name}] 던전 {Id} 입장 요청", _username, _dungeonId);
            Send(PacketId.C2S_EnterDungeon, new C2S_EnterDungeon { DungeonId = _dungeonId });

            // Step 4: 던전 입장 대기 (최대 5초)
            await WaitForCondition(() => _inDungeon, timeoutSec: 5, ct);
            if (!_inDungeon)
            {
                Log.Warning("[DungeonBot:{Name}] 던전 입장 타임아웃", _username);
                return;
            }

            // 서버가 몬스터를 스폰할 시간 대기
            await Task.Delay(500, ct);

            // Step 5: 자동 공격 루프
            Log.Information("[DungeonBot:{Name}] 자동 공격 시작!", _username);
            await AttackLoopAsync(ct);

            await receiveTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "[DungeonBot:{Name}] Error", _username);
        }
        finally
        {
            _client?.Close();
            Log.Information("[DungeonBot:{Name}] 종료", _username);
        }
    }

    /// <summary>
    /// 몬스터가 있을 때마다 C2S_Attack 전송 (쿨다운 1.2초)
    /// </summary>
    private async Task AttackLoopAsync(CancellationToken ct)
    {
        int idleLog = 0;
        while (!ct.IsCancellationRequested && !_isKicked && (_client?.Connected ?? false))
        {
            long targetId = 0;
            lock (_monstersLock)
            {
                if (_monsterIds.Count > 0)
                    targetId = _monsterIds[0];
            }

            if (targetId > 0)
            {
                Log.Information("[DungeonBot:{Name}] C2S_Attack → Entity {Target}", _username, targetId);
                Send(PacketId.C2S_Attack, new C2S_Attack { TargetEntityId = targetId });
            }
            else
            {
                idleLog++;
                if (idleLog % 5 == 1)  // 5초마다 한 번만 로그
                    Log.Information("[DungeonBot:{Name}] 몬스터 없음 — 대기 중...", _username);
            }

            await Task.Delay(1200, ct); // 공격 쿨다운(1초) + 여유
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int n = await _stream.ReadAsync(_readBuf, ct);
                if (n == 0) break;
                for (int i = 0; i < n; i++) _buffer.Add(_readBuf[i]);
                ProcessPackets();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug(ex, "[DungeonBot:{Name}] ReceiveLoop ended", _username);
        }
    }

    private void ProcessPackets()
    {
        while (_buffer.Count >= 4)
        {
            var arr = _buffer.ToArray();
            ushort size = BitConverter.ToUInt16(arr, 0);
            if (_buffer.Count < size) break;

            ushort rawId = BitConverter.ToUInt16(arr, 2);
            byte[] data  = new byte[size - 4];
            Array.Copy(arr, 4, data, 0, data.Length);
            _buffer.RemoveRange(0, size);

            HandlePacket((PacketId)rawId, data);
        }
    }

    private void HandlePacket(PacketId id, byte[] data)
    {
        try
        {
            switch (id)
            {
                // ── 인증 ──────────────────────────────────────────────────────────
                case PacketId.S2C_LoginResult:
                {
                    var p = S2C_LoginResult.Parser.ParseFrom(data);
                    if (p.Success)
                    {
                        Log.Information("[DungeonBot:{Name}] 로그인 성공 → 마을 입장", _username);
                        Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
                    }
                    else
                    {
                        Log.Warning("[DungeonBot:{Name}] 로그인 실패: {Msg}", _username, p.Message);
                    }
                    break;
                }

                case PacketId.S2C_ForceLogout:
                {
                    var p = S2C_ForceLogout.Parser.ParseFrom(data);
                    Log.Warning("[DungeonBot:{Name}] 강제 로그아웃: {Msg}", _username, p.Message);
                    _isKicked = true;
                    try { _stream?.Close(); } catch { }
                    break;
                }

                // ── 마을 ──────────────────────────────────────────────────────────
                case PacketId.S2C_EnterTownResult:
                {
                    var p = S2C_EnterTownResult.Parser.ParseFrom(data);
                    if (p.Success)
                    {
                        _entityId = p.EntityId;
                        _inTown   = true;
                        Log.Information("[DungeonBot:{Name}] 마을 입장 (EntityId={Id})", _username, _entityId);
                    }
                    break;
                }

                // ── 던전 ──────────────────────────────────────────────────────────
                case PacketId.S2C_EnterDungeonResult:
                {
                    var p = S2C_EnterDungeonResult.Parser.ParseFrom(data);
                    if (p.Success)
                    {
                        _entityId  = p.EntityId;
                        _inDungeon = true;
                        Log.Information("[DungeonBot:{Name}] 던전 입장 성공! EntityId={Id}, 주변 엔티티={Count}개",
                            _username, _entityId, p.NearbyEntities.Count);

                        // 이미 스폰된 엔티티 처리
                        foreach (var e in p.NearbyEntities)
                            RegisterEntity(e);
                    }
                    else
                    {
                        Log.Warning("[DungeonBot:{Name}] 던전 입장 실패: {Msg}", _username, p.Message);
                    }
                    break;
                }

                // ── 스폰 / 디스폰 ─────────────────────────────────────────────────
                case PacketId.S2C_Spawn:
                {
                    var p = S2C_Spawn.Parser.ParseFrom(data);
                    if (p.Entity != null)
                        RegisterEntity(p.Entity);
                    break;
                }

                case PacketId.S2C_Despawn:
                {
                    var p = S2C_Despawn.Parser.ParseFrom(data);
                    lock (_monstersLock) _monsterIds.Remove(p.EntityId);
                    Log.Debug("[DungeonBot:{Name}] Despawn Entity={Id}", _username, p.EntityId);
                    break;
                }

                // ── 전투 ──────────────────────────────────────────────────────────
                case PacketId.S2C_Attack:
                {
                    var p = S2C_Attack.Parser.ParseFrom(data);
                    Log.Debug("[DungeonBot:{Name}] [전투] {A} → {T} 공격",
                        _username, p.AttackerEntityId, p.TargetEntityId);
                    break;
                }

                case PacketId.S2C_Damage:
                {
                    var p = S2C_Damage.Parser.ParseFrom(data);
                    // 자신이 맞은 경우 HP 업데이트
                    if (p.TargetEntityId == _entityId)
                    {
                        _hp    = p.CurrentHp;
                        _maxHp = p.MaxHp;
                        Log.Warning("[DungeonBot:{Name}] [전투] 내가 {Dmg} 데미지 받음! HP={Hp}/{Max}",
                            _username, p.Damage, _hp, _maxHp);
                    }
                    else
                    {
                        Log.Information("[DungeonBot:{Name}] [전투] Entity {T} → {Dmg} 데미지, HP={Hp}/{Max}",
                            _username, p.TargetEntityId, p.Damage, p.CurrentHp, p.MaxHp);
                    }
                    break;
                }

                case PacketId.S2C_Death:
                {
                    var p = S2C_Death.Parser.ParseFrom(data);
                    lock (_monstersLock) _monsterIds.Remove(p.EntityId);

                    if (p.KillerEntityId == _entityId)
                        Log.Information("[DungeonBot:{Name}] [전투] Entity {Id} 처치!", _username, p.EntityId);
                    else
                        Log.Information("[DungeonBot:{Name}] [전투] Entity {Id} 사망 (킬러={K})",
                            _username, p.EntityId, p.KillerEntityId);
                    break;
                }

                // ── 보상 ──────────────────────────────────────────────────────────
                case PacketId.S2C_RewardResult:
                {
                    var p = S2C_RewardResult.Parser.ParseFrom(data);
                    _exp  = p.TotalExp;
                    _gold = p.TotalGold;
                    Log.Information("[DungeonBot:{Name}] [보상] +{Exp}EXP +{Gold}G  (누적 EXP={TotalExp}, Gold={TotalGold})",
                        _username, p.ExpReward, p.GoldReward, _exp, _gold);
                    break;
                }

                case PacketId.S2C_LevelUp:
                {
                    var p = S2C_LevelUp.Parser.ParseFrom(data);
                    _level = p.NewLevel;
                    Log.Information("[DungeonBot:{Name}] ★ 레벨업! Lv.{Lv} — HP={Hp}, ATK={Atk}, DEF={Def}",
                        _username, p.NewLevel, p.NewMaxHp, p.NewAttack, p.NewDefense);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DungeonBot:{Name}] HandlePacket error ({Id})", _username, id);
        }
    }

    private void RegisterEntity(EntityInfo e)
    {
        if (e.EntityType == GameShared.Proto.EntityType.Monster)
        {
            lock (_monstersLock)
            {
                if (!_monsterIds.Contains(e.EntityId))
                    _monsterIds.Add(e.EntityId);
            }
            Log.Information("[DungeonBot:{Name}] 몬스터 감지: {Name} (Id={Id}, HP={Hp}/{Max})",
                _username, e.Name, e.EntityId, e.CurrentHp, e.MaxHp);
        }
        else if (e.EntityId != _entityId)
        {
            Log.Debug("[DungeonBot:{Name}] Entity: {Name} (Id={Id})", _username, e.Name, e.EntityId);
        }
    }

    private static async Task WaitForCondition(Func<bool> condition, int timeoutSec, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (!condition() && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            await Task.Delay(100, ct);
    }

    private void Send(PacketId packetId, IMessage packet)
    {
        if (_stream == null || _isKicked) return;
        try
        {
            byte[] data = packet.ToByteArray();
            ushort size = (ushort)(4 + data.Length);
            byte[] buf  = new byte[size];
            BitConverter.TryWriteBytes(buf.AsSpan(0, 2), size);
            BitConverter.TryWriteBytes(buf.AsSpan(2, 2), (ushort)packetId);
            data.CopyTo(buf, 4);
            _stream.Write(buf);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DungeonBot:{Name}] Send error", _username);
        }
    }
}
