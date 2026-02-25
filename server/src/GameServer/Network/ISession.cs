using GameShared.Enums;
using Google.Protobuf;

namespace GameServer.Network;

/// <summary>
/// Common interface for Session and SessionAsync
/// </summary>
public interface ISession
{
    long SessionId { get; }
    long? PlayerId { get; set; }
    string PlayerName { get; set; }
    bool IsConnected { get; }

    void Send(PacketId packetId, IMessage packet);
    void Disconnect();
}
