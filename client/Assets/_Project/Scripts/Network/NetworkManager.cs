using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Google.Protobuf;
using GameShared.Enums;

/// <summary>
/// 서버와의 TCP 연결을 관리하는 싱글톤 매니저 (Protobuf 버전)
///
/// 스레딩 모델:
///   - 네트워크 스레드: OnReceive → ProcessPackets → EnqueueMainThread 까지만 실행
///   - 메인 스레드: Update()에서 큐를 drain하여 HandlePacket → 핸들러 → 이벤트 호출
///   - 따라서 모든 이벤트 콜백은 메인 스레드에서 안전하게 Unity API 접근 가능
///
/// 핸들러 등록:
///   [PacketHandler(PacketId.X)] 어트리뷰트가 붙은 메서드를 Awake 시 리플렉션으로 자동 등록.
///   핸들러 구현은 Handlers/ 폴더의 partial class 파일들에 분산되어 있다.
/// </summary>
public partial class NetworkManager : MonoBehaviour
{
    // ── 싱글톤 ──────────────────────────────────────────────────────────────

    private static NetworkManager _instance;
    public static NetworkManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<NetworkManager>();
                if (_instance == null)
                {
                    var go = new GameObject("NetworkManager");
                    _instance = go.AddComponent<NetworkManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    // ── 설정 ─────────────────────────────────────────────────────────────────

    private const int ReceiveBufferSize    = 8192;
    private const int PacketBufferSize     = 65536;
    private const int HeaderSize           = 4;       // [Size 2B][PacketId 2B]
    private const int MaxPacketSize        = 65535;    // ushort.MaxValue
    private const int MaxMainThreadQueue   = 4096;

    // ── 네트워크 ─────────────────────────────────────────────────────────────

    private volatile TcpClient _client;
    private volatile NetworkStream _stream;

    // 수신 버퍼 (네트워크 스레드 전용 — lock 불필요)
    private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];

    // 패킷 조립 버퍼 (네트워크 스레드 전용 — 오프셋 기반, GC 할당 없음)
    private readonly byte[] _packetBuffer = new byte[PacketBufferSize];
    private int _packetBufferLen;

    // 메인 스레드 큐 (_queueLock 으로 보호)
    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
    private readonly object _queueLock = new object();

    // ── 상태 ─────────────────────────────────────────────────────────────────

    public bool IsConnected => _client is { Connected: true };

    // ── 연결 이벤트 (코어에서 직접 발생) ────────────────────────────────────

    public event Action OnConnectedToServer;
    public event Action OnDisconnectedFromServer;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        RegisterHandlers();
    }

    void Update()
    {
        lock (_queueLock)
        {
            while (_mainThreadQueue.Count > 0)
                _mainThreadQueue.Dequeue()?.Invoke();
        }
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }

    // ── 연결 ─────────────────────────────────────────────────────────────────

    public void Connect(string host, int port)
    {
        try
        {
            Debug.Log($"[NetworkManager] Connecting to {host}:{port}...");
            _client = new TcpClient();
            _client.BeginConnect(host, port, OnConnected, null);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Connect failed: {ex.Message}");
        }
    }

    private void OnConnected(IAsyncResult ar)
    {
        try
        {
            _client.EndConnect(ar);
            _stream = _client.GetStream();
            _stream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnReceive, null);
            Debug.Log("[NetworkManager] Connected to server");
            EnqueueMainThread(() => OnConnectedToServer?.Invoke());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] OnConnected failed: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        var stream = _stream;
        var client = _client;
        _stream = null;
        _client = null;

        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }

        // 네트워크 스레드에서 쌓인 stale 큐 제거
        lock (_queueLock)
            _mainThreadQueue.Clear();

        _packetBufferLen = 0;

        EnqueueMainThread(() => OnDisconnectedFromServer?.Invoke());
        Debug.Log("[NetworkManager] Disconnected");
    }

    // ── 수신 (네트워크 스레드) ───────────────────────────────────────────────

    private void OnReceive(IAsyncResult ar)
    {
        try
        {
            var stream = _stream;
            if (stream == null) return;  // 이미 Disconnect 된 경우

            int bytesRead = stream.EndRead(ar);
            if (bytesRead <= 0)
            {
                Debug.Log("[NetworkManager] Connection closed by server");
                Disconnect();
                return;
            }

            AppendToPacketBuffer(bytesRead);
            ProcessPackets();
            stream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnReceive, null);
        }
        catch (ObjectDisposedException)
        {
            // Disconnect 후 콜백이 도착한 경우 — 정상 흐름
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] OnReceive failed: {ex.Message}");
            Disconnect();
        }
    }

    /// <summary>
    /// _receiveBuffer → _packetBuffer 로 수신 데이터를 복사한다.
    /// Buffer.BlockCopy 를 사용하여 바이트 단위 루프보다 빠르다.
    /// </summary>
    private void AppendToPacketBuffer(int bytesRead)
    {
        if (_packetBufferLen + bytesRead > PacketBufferSize)
        {
            Debug.LogError("[NetworkManager] Packet buffer overflow, disconnecting");
            Disconnect();
            return;
        }
        Buffer.BlockCopy(_receiveBuffer, 0, _packetBuffer, _packetBufferLen, bytesRead);
        _packetBufferLen += bytesRead;
    }

    /// <summary>
    /// 패킷 버퍼에서 완성된 패킷을 파싱해 메인 스레드 큐에 넣는다.
    /// 오프셋 기반이므로 ToArray() 할당이 발생하지 않는다.
    /// 헤더: [Size 2 bytes LE][PacketId 2 bytes LE]  Size 는 헤더+바디 합계.
    /// </summary>
    private void ProcessPackets()
    {
        int offset = 0;

        while (offset + HeaderSize <= _packetBufferLen)
        {
            ushort size = BitConverter.ToUInt16(_packetBuffer, offset);

            if (size < HeaderSize)
            {
                Debug.LogError("[NetworkManager] Invalid packet size, disconnecting");
                Disconnect();
                return;
            }

            if (size > MaxPacketSize)
            {
                Debug.LogError($"[NetworkManager] Packet too large ({size} bytes), disconnecting");
                Disconnect();
                return;
            }

            if (offset + size > _packetBufferLen)
                break;  // 아직 완성되지 않은 패킷

            ushort   packetIdValue = BitConverter.ToUInt16(_packetBuffer, offset + 2);
            PacketId packetId      = (PacketId)packetIdValue;

            int    dataSize = size - HeaderSize;
            byte[] data     = new byte[dataSize];
            Buffer.BlockCopy(_packetBuffer, offset + HeaderSize, data, 0, dataSize);

            EnqueueMainThread(() => HandlePacket(packetId, data));

            offset += size;
        }

        // 남은 미완성 데이터를 버퍼 앞으로 이동
        int remaining = _packetBufferLen - offset;
        if (remaining > 0)
            Buffer.BlockCopy(_packetBuffer, offset, _packetBuffer, 0, remaining);
        _packetBufferLen = remaining;
    }

    // ── 송신 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 패킷을 직렬화하여 서버로 전송한다.
    /// [Size 2 bytes LE][PacketId 2 bytes LE][Protobuf data]
    /// </summary>
    public void Send(PacketId packetId, IMessage packet)
    {
        var stream = _stream;
        if (stream == null)
        {
            Debug.LogError("[NetworkManager] Not connected");
            return;
        }

        try
        {
            byte[] data      = packet.ToByteArray();
            int    totalSize = HeaderSize + data.Length;

            if (totalSize > MaxPacketSize)
            {
                Debug.LogError($"[NetworkManager] Packet too large ({totalSize} bytes), dropping");
                return;
            }

            byte[] buf = new byte[totalSize];
            BitConverter.GetBytes((ushort)totalSize).CopyTo(buf, 0);
            BitConverter.GetBytes((ushort)packetId).CopyTo(buf, 2);
            Buffer.BlockCopy(data, 0, buf, HeaderSize, data.Length);

            stream.Write(buf, 0, buf.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Send failed: {ex.Message}");
        }
    }

    // ── 핸들러 등록 (어트리뷰트 기반 자동 등록) ─────────────────────────────

    private readonly Dictionary<PacketId, Action<byte[]>> _handlers = new Dictionary<PacketId, Action<byte[]>>();

    /// <summary>
    /// [PacketHandler] 어트리뷰트가 붙은 모든 메서드를 탐색하여 _handlers 에 등록한다.
    /// Awake() 시 한 번만 호출되며, 이후 패킷 디스패치는 Dictionary O(1) 조회만 사용한다.
    /// </summary>
    private void RegisterHandlers()
    {
        var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<PacketHandlerAttribute>();
            if (attr == null) continue;

            if (_handlers.ContainsKey(attr.PacketId))
            {
                Debug.LogError($"[NetworkManager] Duplicate handler for {attr.PacketId}: {method.Name}");
                continue;
            }

            try
            {
                var handler = (Action<byte[]>)Delegate.CreateDelegate(typeof(Action<byte[]>), this, method);
                _handlers[attr.PacketId] = handler;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] Failed to register {method.Name} for {attr.PacketId}: {ex.Message}");
            }
        }

        Debug.Log($"[NetworkManager] Registered {_handlers.Count} packet handlers");
    }

    private void HandlePacket(PacketId packetId, byte[] data)
    {
        if (!_handlers.TryGetValue(packetId, out var handler))
        {
            Debug.LogWarning($"[NetworkManager] Unhandled packet: {packetId}");
            return;
        }

        try
        {
            handler(data);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Handler error for {packetId}: {ex.Message}");
        }
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────

    private void EnqueueMainThread(Action action)
    {
        lock (_queueLock)
        {
            if (_mainThreadQueue.Count >= MaxMainThreadQueue)
            {
                Debug.LogWarning($"[NetworkManager] Main thread queue overflow ({_mainThreadQueue.Count}), dropping oldest packets");
                // 절반을 버려서 최신 패킷이 처리되도록 한다
                int dropCount = _mainThreadQueue.Count / 2;
                for (int i = 0; i < dropCount; i++)
                    _mainThreadQueue.Dequeue();
            }
            _mainThreadQueue.Enqueue(action);
        }
    }
}
