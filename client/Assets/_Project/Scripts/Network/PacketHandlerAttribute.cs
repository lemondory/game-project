using System;
using GameShared.Enums;

/// <summary>
/// 이 어트리뷰트를 핸들러 메서드에 붙이면 NetworkManager.RegisterHandlers()에서 자동 등록된다.
/// 메서드 시그니처는 반드시 Action&lt;byte[]&gt; 이어야 한다.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PacketHandlerAttribute : Attribute
{
    public PacketId PacketId { get; }
    public PacketHandlerAttribute(PacketId packetId) => PacketId = packetId;
}
