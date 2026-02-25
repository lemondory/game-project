using MessagePack;

namespace GameShared.Packets;

[MessagePackObject]
public class C2S_Chat
{
    [Key(0)]
    public string Message { get; set; } = string.Empty;
}

[MessagePackObject]
public class S2C_Chat
{
    [Key(0)]
    public string SenderName { get; set; } = string.Empty;

    [Key(1)]
    public string Message { get; set; } = string.Empty;
}
