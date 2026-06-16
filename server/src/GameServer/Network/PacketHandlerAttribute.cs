using GameShared.Enums;

namespace GameServer.Network;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class PacketHandlerAttribute : Attribute
{
    public PacketId PacketId { get; }
    public PacketHandlerAttribute(PacketId packetId) => PacketId = packetId;
}
