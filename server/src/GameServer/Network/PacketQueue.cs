using System.Collections.Concurrent;
using GameShared.Enums;

namespace GameServer.Network;

public class PacketQueue
{
    private readonly ConcurrentQueue<PacketMessage> _queue = new();

    public void Enqueue(ISession session, PacketId packetId, byte[] data)
    {
        _queue.Enqueue(new PacketMessage(session, packetId, data));
    }

    public bool TryDequeue(out PacketMessage message)
    {
        return _queue.TryDequeue(out message);
    }

    public int Count => _queue.Count;
}

public readonly record struct PacketMessage(ISession Session, PacketId PacketId, byte[] Data);
