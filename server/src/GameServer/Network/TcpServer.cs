using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GameShared.Enums;
using Serilog;

namespace GameServer.Network;

/// <summary>
/// Accepts TCP connections and creates Session instances.
/// Each session manages its own async receive/send pipeline.
/// </summary>
public class TcpServer
{
    private readonly Socket _listenSocket;
    private readonly ConcurrentDictionary<long, Session> _sessions = new();
    private readonly PacketQueue _packetQueue;
    private long _nextSessionId = 1;
    private volatile bool _isRunning;

    public PacketQueue PacketQueue => _packetQueue;

    /// <summary>세션이 연결 해제될 때 발생 (PacketHandler 가 매핑 정리에 사용)</summary>
    public event Action<ISession>? SessionDisconnected;

    public TcpServer(int port)
    {
        _packetQueue = new PacketQueue();
        _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listenSocket.NoDelay = true;
        _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listenSocket.Bind(new IPEndPoint(IPAddress.Any, port));
        _listenSocket.Listen(backlog: 1000);
        Log.Information("TcpServer: listening on port {Port}", port);
    }

    public void Start()
    {
        _isRunning = true;
        _ = AcceptLoopAsync();
        Log.Information("TcpServer: started");
    }

    public void Stop()
    {
        _isRunning = false;
        _listenSocket.Close();

        foreach (var session in _sessions.Values)
            session.Disconnect();

        _sessions.Clear();
        Log.Information("TcpServer: stopped");
    }

    private async Task AcceptLoopAsync()
    {
        while (_isRunning)
        {
            try
            {
                var clientSocket = await _listenSocket.AcceptAsync();
                clientSocket.NoDelay = true;

                long id = Interlocked.Increment(ref _nextSessionId);
                var session = new Session(id, clientSocket);
                session.OnPacketReceived += OnPacketReceived;
                session.OnDisconnected   += OnSessionDisconnected;

                _sessions[id] = session;
                session.Start();

                Log.Information("TcpServer: session {Id} connected from {Remote} (total: {Count})",
                    id, clientSocket.RemoteEndPoint, _sessions.Count);
            }
            catch (Exception ex) when (_isRunning)
            {
                Log.Error(ex, "TcpServer: accept error");
            }
        }
    }

    private void OnPacketReceived(Session session, PacketId packetId, byte[] data)
    {
        _packetQueue.Enqueue(session, packetId, data);
    }

    private void OnSessionDisconnected(Session session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        SessionDisconnected?.Invoke(session);
        Log.Information("TcpServer: session {Id} removed (remaining: {Count})",
            session.SessionId, _sessions.Count);
    }
}
