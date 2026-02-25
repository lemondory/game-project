using System;
using System.Net.Sockets;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Google.Protobuf;
using GameShared.Enums;
using GameShared.Proto;

/// <summary>
/// 서버와의 TCP 연결을 관리하는 싱글톤 매니저 (Protobuf 버전)
/// 패킷 수신은 네트워크 스레드에서 이루어지며, 핸들러 호출은 메인 스레드 큐를 통해 Unity 안전하게 처리한다.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    // ── 싱글톤 ──────────────────────────────────────────────────────────────

    private static NetworkManager _instance;
    public static NetworkManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<NetworkManager>();
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

    // ── 네트워크 ─────────────────────────────────────────────────────────────

    private TcpClient _client;
    private NetworkStream _stream;

    // 수신 버퍼 (네트워크 스레드 전용)
    private readonly byte[] _receiveBuffer = new byte[8192];
    private readonly List<byte> _packetBuffer = new List<byte>();

    // 메인 스레드 큐 (_queueLock 으로 보호)
    private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
    private readonly object _queueLock = new object();

    // ── 상태 ─────────────────────────────────────────────────────────────────

    public bool IsConnected => _client != null && _client.Connected;

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    public event Action OnConnectedToServer;
    public event Action OnDisconnectedFromServer;

    public event Action<S2C_EnterTownResult> OnEnterTownReceived;
    public event Action<S2C_Spawn>           OnSpawnReceived;
    public event Action<S2C_Despawn>         OnDespawnReceived;
    public event Action<S2C_Move>            OnMoveReceived;
    public event Action<S2C_Chat>            OnChatReceived;

    /// <summary>Town 씬 로드 시 TownManager 가 읽어갈 입장 데이터</summary>
    public S2C_EnterTownResult PendingEnterTownResult { get; private set; }

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
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
        EnqueueMainThread(() => OnDisconnectedFromServer?.Invoke());
        Debug.Log("[NetworkManager] Disconnected");
    }

    // ── 수신 ─────────────────────────────────────────────────────────────────

    private void OnReceive(IAsyncResult ar)
    {
        try
        {
            int bytesRead = _stream.EndRead(ar);
            if (bytesRead > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                    _packetBuffer.Add(_receiveBuffer[i]);

                ProcessPackets();
                _stream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnReceive, null);
            }
            else
            {
                Debug.Log("[NetworkManager] Connection closed by server");
                Disconnect();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] OnReceive failed: {ex.Message}");
            Disconnect();
        }
    }

    /// <summary>
    /// 버퍼에서 완성된 패킷을 파싱해 메인 스레드 큐에 넣는다.
    /// 헤더: [Size 2 bytes LE][PacketId 2 bytes LE]  Size 는 헤더+바디 합계.
    /// </summary>
    private void ProcessPackets()
    {
        while (_packetBuffer.Count >= 4)
        {
            ushort size = BitConverter.ToUInt16(_packetBuffer.ToArray(), 0);

            // 최소 크기 검증 — size < 4 이면 ushort 언더플로우 방지
            if (size < 4)
            {
                Debug.LogError("[NetworkManager] Invalid packet size, disconnecting");
                Disconnect();
                return;
            }

            if (_packetBuffer.Count < size)
                break; // 아직 완성되지 않은 패킷

            ushort   packetIdValue = BitConverter.ToUInt16(_packetBuffer.ToArray(), 2);
            PacketId packetId      = (PacketId)packetIdValue;

            int    dataSize = size - 4;
            byte[] data     = new byte[dataSize];
            _packetBuffer.CopyTo(4, data, 0, dataSize);
            _packetBuffer.RemoveRange(0, size);

            byte[] dataCopy = (byte[])data.Clone();
            EnqueueMainThread(() => HandlePacket(packetId, dataCopy));
        }
    }

    // ── 송신 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 패킷을 직렬화하여 서버로 전송한다.
    /// [Size 2 bytes LE][PacketId 2 bytes LE][Protobuf data]
    /// </summary>
    public void Send(PacketId packetId, IMessage packet)
    {
        if (!IsConnected)
        {
            Debug.LogError("[NetworkManager] Not connected");
            return;
        }

        try
        {
            byte[] data      = packet.ToByteArray();
            int    totalSize = 4 + data.Length;

            if (totalSize > ushort.MaxValue)
            {
                Debug.LogError($"[NetworkManager] Packet too large ({totalSize} bytes), dropping");
                return;
            }

            byte[] buf = new byte[totalSize];
            BitConverter.GetBytes((ushort)totalSize).CopyTo(buf, 0);
            BitConverter.GetBytes((ushort)packetId).CopyTo(buf, 2);
            data.CopyTo(buf, 4);

            _stream.Write(buf, 0, buf.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Send failed: {ex.Message}");
        }
    }

    // ── 패킷 핸들러 등록 ──────────────────────────────────────────────────────

    private readonly Dictionary<PacketId, Action<byte[]>> _handlers = new Dictionary<PacketId, Action<byte[]>>();

    private void RegisterHandlers()
    {
        _handlers[PacketId.S2C_LoginResult]     = OnLoginResult;
        _handlers[PacketId.S2C_EnterTownResult] = OnEnterTownResult;
        _handlers[PacketId.S2C_Spawn]           = OnSpawn;
        _handlers[PacketId.S2C_Despawn]         = OnDespawn;
        _handlers[PacketId.S2C_Move]            = OnMove;
        _handlers[PacketId.S2C_Chat]            = OnChat;
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
            // Protobuf 파싱 실패 또는 핸들러 내 예외 — 크래시 방지
            Debug.LogError($"[NetworkManager] Handler error for {packetId}: {ex.Message}");
        }
    }

    // ── 패킷 핸들러 구현 ──────────────────────────────────────────────────────

    private void OnLoginResult(byte[] data)
    {
        var packet = S2C_LoginResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Login: success={packet.Success}, msg={packet.Message}");
        if (packet.Success)
            Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
    }

    private void OnEnterTownResult(byte[] data)
    {
        var packet = S2C_EnterTownResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] EnterTown: success={packet.Success}, entityId={packet.EntityId}");
        if (packet.Success)
        {
            PendingEnterTownResult = packet;
            SceneManager.LoadScene("Town");
        }
    }

    private void OnSpawn(byte[] data)
    {
        var packet = S2C_Spawn.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Spawn: EntityId={packet.Entity?.EntityId}, Name={packet.Entity?.Name}");
        OnSpawnReceived?.Invoke(packet);
    }

    private void OnDespawn(byte[] data)
    {
        var packet = S2C_Despawn.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Despawn: EntityId={packet.EntityId}");
        OnDespawnReceived?.Invoke(packet);
    }

    private void OnMove(byte[] data)
    {
        var packet = S2C_Move.Parser.ParseFrom(data);
        OnMoveReceived?.Invoke(packet);
    }

    private void OnChat(byte[] data)
    {
        var packet = S2C_Chat.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Chat: {packet.SenderName}: {packet.Message}");
        OnChatReceived?.Invoke(packet);
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────

    private void EnqueueMainThread(Action action)
    {
        lock (_queueLock)
            _mainThreadQueue.Enqueue(action);
    }
}
