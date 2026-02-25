using System.Net.Sockets;
using GameShared.Enums;
using MessagePack;
using Serilog;

namespace GameClient.Network;

public class NetworkManager
{
    private readonly Socket _socket;
    private readonly byte[] _receiveBuffer = new byte[8192];
    private readonly MemoryStream _receiveStream = new();
    private bool _isConnected;

    public bool IsConnected => _isConnected;

    public event Action<PacketId, byte[]>? OnPacketReceived;

    public NetworkManager()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    public bool Connect(string host, int port)
    {
        try
        {
            _socket.Connect(host, port);
            _isConnected = true;
            StartReceive();
            Log.Information("Connected to {Host}:{Port}", host, port);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to {Host}:{Port}", host, port);
            return false;
        }
    }

    private void StartReceive()
    {
        if (!_isConnected)
            return;

        _socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, OnReceiveCallback, null);
    }

    private void OnReceiveCallback(IAsyncResult ar)
    {
        try
        {
            int bytesRead = _socket.EndReceive(ar);
            if (bytesRead == 0)
            {
                Disconnect();
                return;
            }

            _receiveStream.Write(_receiveBuffer, 0, bytesRead);
            ProcessPackets();

            if (_isConnected)
            {
                _socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, OnReceiveCallback, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Receive error");
            Disconnect();
        }
    }

    private void ProcessPackets()
    {
        _receiveStream.Position = 0;

        Span<byte> header = stackalloc byte[4]; // Moved outside loop to avoid stack overflow

        while (_receiveStream.Position + 4 <= _receiveStream.Length)
        {
            long startPosition = _receiveStream.Position;

            _receiveStream.Read(header);

            ushort packetSize = BitConverter.ToUInt16(header);
            ushort packetId = BitConverter.ToUInt16(header[2..]);

            if (_receiveStream.Position + packetSize - 4 > _receiveStream.Length)
            {
                _receiveStream.Position = startPosition;
                break;
            }

            byte[] packetData = new byte[packetSize - 4];
            _receiveStream.Read(packetData, 0, packetData.Length);

            try
            {
                OnPacketReceived?.Invoke((PacketId)packetId, packetData);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing packet {PacketId}", (PacketId)packetId);
            }
        }

        if (_receiveStream.Position > 0)
        {
            byte[] remaining = new byte[_receiveStream.Length - _receiveStream.Position];
            _receiveStream.Read(remaining, 0, remaining.Length);
            _receiveStream.SetLength(0);
            _receiveStream.Write(remaining);
        }
    }

    public void Send<T>(PacketId packetId, T packet)
    {
        try
        {
            byte[] data = MessagePackSerializer.Serialize(packet);
            ushort packetSize = (ushort)(data.Length + 4);

            byte[] sendData = new byte[packetSize];
            BitConverter.TryWriteBytes(sendData.AsSpan(0, 2), packetSize);
            BitConverter.TryWriteBytes(sendData.AsSpan(2, 2), (ushort)packetId);
            Array.Copy(data, 0, sendData, 4, data.Length);

            _socket.Send(sendData);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send packet {PacketId}", packetId);
        }
    }

    public void Disconnect()
    {
        if (!_isConnected)
            return;

        _isConnected = false;

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
        }
        catch { }

        Log.Information("Disconnected from server");
    }
}
