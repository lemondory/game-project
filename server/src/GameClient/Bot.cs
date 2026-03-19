using System.Net.Sockets;
using GameShared.Enums;
using GameShared.Proto;
using Google.Protobuf;
using Serilog;

namespace GameClient;

/// <summary>
/// Protobuf 기반 자율 이동 봇
/// 로그인 → 마을 입장 → 랜덤 이동 + 주기적 채팅
/// </summary>
public class Bot
{
    private readonly string _username;
    private readonly string _password;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly List<byte> _buffer = new();
    private readonly byte[] _readBuf = new byte[4096];

    private long _entityId;
    private bool _inTown;
    private volatile bool _isKicked;
    private readonly Random _rng = new();
    private readonly float  _moveRange; // 이동 범위 ±값 (기본 8f)

    public string Name => _username;

    public Bot(string username, string password = "test", float moveRange = 8f)
    {
        _username  = username;
        _password  = password;
        _moveRange = moveRange;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync("127.0.0.1", 7777, ct);
            _stream = _client.GetStream();
            Log.Information("[Bot:{Name}] Connected", _username);

            Send(PacketId.C2S_Login, new C2S_Login { Username = _username, Password = _password });

            // 수신 루프를 백그라운드로 실행
            var receiveTask = ReceiveLoopAsync(ct);

            // 마을 입장 대기 (최대 5초)
            var waitUntil = DateTime.UtcNow.AddSeconds(5);
            while (!_inTown && DateTime.UtcNow < waitUntil && !ct.IsCancellationRequested)
                await Task.Delay(100, ct);

            if (!_inTown)
            {
                Log.Warning("[Bot:{Name}] Did not enter town in time", _username);
                return;
            }

            // 행동 루프: 이동 + 채팅
            int chatTick = 0;
            while (!ct.IsCancellationRequested && !_isKicked && (_client?.Connected ?? false))
            {
                await Task.Delay(2000 + _rng.Next(1000), ct);

                // 랜덤 위치로 이동 (-moveRange ~ +moveRange 범위)
                float x = (float)(_rng.NextDouble() * _moveRange * 2 - _moveRange);
                float z = (float)(_rng.NextDouble() * _moveRange * 2 - _moveRange);
                Send(PacketId.C2S_Move, new C2S_Move
                {
                    Destination = new Vec3 { X = x, Y = 0, Z = z }
                });

                // 5회 이동마다 채팅
                chatTick++;
                if (chatTick % 5 == 0)
                {
                    Send(PacketId.C2S_Chat, new C2S_Chat
                    {
                        Message = $"Hello from {_username}! ({DateTime.Now:HH:mm:ss})"
                    });
                }
            }

            await receiveTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "[Bot:{Name}] Error", _username);
        }
        finally
        {
            _client?.Close();
            Log.Information("[Bot:{Name}] Stopped", _username);
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
            Log.Debug(ex, "[Bot:{Name}] ReceiveLoop ended", _username);
        }
    }

    private void ProcessPackets()
    {
        while (_buffer.Count >= 4)
        {
            var arr = _buffer.ToArray();
            ushort size = BitConverter.ToUInt16(arr, 0);
            if (_buffer.Count < size) break;

            ushort id = BitConverter.ToUInt16(arr, 2);
            byte[] data = new byte[size - 4];
            Array.Copy(arr, 4, data, 0, data.Length);
            _buffer.RemoveRange(0, size);

            HandlePacket((PacketId)id, data);
        }
    }

    private void HandlePacket(PacketId id, byte[] data)
    {
        try
        {
            switch (id)
            {
                case PacketId.S2C_LoginResult:
                    var login = S2C_LoginResult.Parser.ParseFrom(data);
                    if (login.Success)
                    {
                        Log.Information("[Bot:{Name}] Login success → entering town", _username);
                        Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
                    }
                    else
                    {
                        Log.Warning("[Bot:{Name}] Login failed: {Msg}", _username, login.Message);
                    }
                    break;

                case PacketId.S2C_ForceLogout:
                    var kicked = S2C_ForceLogout.Parser.ParseFrom(data);
                    Log.Warning("[Bot:{Name}] 강제 로그아웃: {Msg}", _username, kicked.Message);
                    _isKicked = true;
                    try { _stream?.Close(); } catch { }
                    break;

                case PacketId.S2C_EnterTownResult:
                    var town = S2C_EnterTownResult.Parser.ParseFrom(data);
                    if (town.Success)
                    {
                        _entityId = town.EntityId;
                        _inTown = true;
                        Log.Information("[Bot:{Name}] Entered town (EntityId={Id})", _username, _entityId);
                    }
                    break;

                case PacketId.S2C_Spawn:
                    var spawn = S2C_Spawn.Parser.ParseFrom(data);
                    Log.Debug("[Bot:{Name}] Spawn: {SpawnName} ({SpawnId})", _username, spawn.Entity?.Name, spawn.Entity?.EntityId);
                    break;

                case PacketId.S2C_Chat:
                    var chat = S2C_Chat.Parser.ParseFrom(data);
                    Log.Information("[Bot:{Name}] Chat → [{Sender}] {Msg}", _username, chat.SenderName, chat.Message);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Bot:{Name}] HandlePacket error for {Id}", _username, id);
        }
    }

    private void Send(PacketId packetId, IMessage packet)
    {
        if (_stream == null || _isKicked) return;
        try
        {
            byte[] data = packet.ToByteArray();
            ushort size = (ushort)(4 + data.Length);
            byte[] buf = new byte[size];
            BitConverter.TryWriteBytes(buf.AsSpan(0, 2), size);
            BitConverter.TryWriteBytes(buf.AsSpan(2, 2), (ushort)packetId);
            data.CopyTo(buf, 4);
            _stream.Write(buf);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Bot:{Name}] Send error", _username);
        }
    }
}
