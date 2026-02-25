using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;
using GameShared.Enums;
using Google.Protobuf;
using Serilog;

namespace GameServer.Network;

/// <summary>
/// High-performance TCP session using System.IO.Pipelines for receive
/// and System.Threading.Channels for send.
///
/// Receive pipeline:
///   Socket.ReceiveAsync → Pipe.Writer → Pipe.Reader (frame parsing)
///
/// Send pipeline:
///   Send() → Channel.Writer → Channel.Reader → Socket.SendAsync
/// </summary>
public sealed class Session : ISession
{
    private const int MaxPacketBodySize = 64 * 1024; // 64 KB per packet body
    private const int SendChannelCapacity = 4096;

    private readonly Socket _socket;
    private readonly Pipe _receivePipe = new();
    private readonly Channel<byte[]> _sendChannel =
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(SendChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // 느린 클라이언트 패킷 드롭
            SingleReader = true
        });

    private readonly CancellationTokenSource _cts = new();
    private volatile bool _isConnected = true;

    public long SessionId { get; }
    public long? PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool IsConnected => _isConnected;

    public event Action<Session, PacketId, byte[]>? OnPacketReceived;
    public event Action<Session>? OnDisconnected;

    public Session(long sessionId, Socket socket)
    {
        SessionId = sessionId;
        _socket = socket;
    }

    /// <summary>수신/송신 루프를 시작한다 (fire-and-forget)</summary>
    public void Start()
    {
        var ct = _cts.Token;

        _ = ReceiveLoopAsync(ct)
              .ContinueWith(t => Log.Error(t.Exception?.GetBaseException(), "Session {Id}: receive error", SessionId),
                            TaskContinuationOptions.OnlyOnFaulted);

        _ = ParseLoopAsync(ct)
              .ContinueWith(t => Log.Error(t.Exception?.GetBaseException(), "Session {Id}: parse error", SessionId),
                            TaskContinuationOptions.OnlyOnFaulted);

        _ = SendLoopAsync(ct)
              .ContinueWith(t => Log.Error(t.Exception?.GetBaseException(), "Session {Id}: send error", SessionId),
                            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ── Receive ─────────────────────────────────────────────────────────────

    /// <summary>소켓에서 데이터를 읽어 Pipe.Writer 에 쓴다</summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var writer = _receivePipe.Writer;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var memory = writer.GetMemory(8192);
                int bytesReceived = await _socket.ReceiveAsync(memory, SocketFlags.None, ct);

                if (bytesReceived == 0)
                    break; // 연결 종료

                writer.Advance(bytesReceived);

                var flushResult = await writer.FlushAsync(ct);
                if (flushResult.IsCompleted)
                    break; // Reader 가 완료됨 → 종료
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex)
        {
            Log.Debug("Session {Id}: socket closed ({Error})", SessionId, ex.SocketErrorCode);
        }
        finally
        {
            await writer.CompleteAsync();
            Disconnect();
        }
    }

    /// <summary>Pipe.Reader 에서 완성된 패킷을 파싱해 핸들러에 전달한다</summary>
    private async Task ParseLoopAsync(CancellationToken ct)
    {
        var reader = _receivePipe.Reader;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                while (TryParsePacket(ref buffer, out var packetId, out var data))
                {
                    try
                    {
                        OnPacketReceived?.Invoke(this, packetId, data);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Session {Id}: error in packet handler {PacketId}", SessionId, packetId);
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            reader.Complete();
        }
    }

    /// <summary>
    /// 헤더: [Size 2 bytes LE][PacketId 2 bytes LE] + Body[Size-4 bytes]
    /// Size 는 헤더+바디 합계.
    /// </summary>
    private bool TryParsePacket(ref ReadOnlySequence<byte> buffer,
                                out PacketId packetId, out byte[] data)
    {
        packetId = default;
        data = Array.Empty<byte>();

        if (buffer.Length < 4)
            return false;

        Span<byte> header = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(header);

        ushort totalSize = BitConverter.ToUInt16(header);
        ushort rawId    = BitConverter.ToUInt16(header[2..]);

        // 최소 크기 검증 (4 = 헤더만)
        if (totalSize < 4)
        {
            Log.Warning("Session {Id}: invalid packet size {Size}, disconnecting", SessionId, totalSize);
            Disconnect();
            return false;
        }

        // 최대 바디 크기 검증
        int bodySize = totalSize - 4;
        if (bodySize > MaxPacketBodySize)
        {
            Log.Warning("Session {Id}: packet body too large ({Size}), disconnecting", SessionId, bodySize);
            Disconnect();
            return false;
        }

        // 패킷 전체가 수신되지 않았으면 대기
        if (buffer.Length < totalSize)
            return false;

        data = new byte[bodySize];
        buffer.Slice(4, bodySize).CopyTo(data);
        buffer = buffer.Slice(totalSize);

        packetId = (PacketId)rawId;
        return true;
    }

    // ── Send ─────────────────────────────────────────────────────────────────

    /// <summary>Channel.Reader 에서 패킷을 꺼내 소켓으로 전송한다</summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var data in _sendChannel.Reader.ReadAllAsync(ct))
            {
                await _socket.SendAsync(data.AsMemory(), SocketFlags.None, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException ex)
        {
            Log.Debug("Session {Id}: send socket error ({Error})", SessionId, ex.SocketErrorCode);
        }
        finally
        {
            Disconnect();
        }
    }

    /// <summary>
    /// 패킷을 직렬화하여 송신 채널에 넣는다.
    /// Thread-safe: 어느 스레드에서나 호출 가능.
    /// </summary>
    public void Send(PacketId packetId, IMessage packet)
    {
        if (!_isConnected)
            return;

        try
        {
            byte[] body = packet.ToByteArray();
            int totalSize = 4 + body.Length;

            if (totalSize > ushort.MaxValue)
            {
                Log.Error("Session {Id}: packet too large to send ({Size})", SessionId, totalSize);
                return;
            }

            byte[] buf = new byte[totalSize];
            BitConverter.TryWriteBytes(buf.AsSpan(0, 2), (ushort)totalSize);
            BitConverter.TryWriteBytes(buf.AsSpan(2, 2), (ushort)packetId);
            body.CopyTo(buf, 4);

            _sendChannel.Writer.TryWrite(buf);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: failed to serialize/enqueue packet {PacketId}", SessionId, packetId);
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public void Disconnect()
    {
        if (!_isConnected)
            return;

        _isConnected = false;
        _cts.Cancel();
        _sendChannel.Writer.TryComplete();

        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket.Close(); } catch { }

        OnDisconnected?.Invoke(this);
        Log.Information("Session {Id}: disconnected", SessionId);
    }
}
