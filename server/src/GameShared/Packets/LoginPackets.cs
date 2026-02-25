using MessagePack;

namespace GameShared.Packets;

[MessagePackObject]
public class C2S_Login
{
    [Key(0)]
    public string Username { get; set; } = string.Empty;

    [Key(1)]
    public string Password { get; set; } = string.Empty;
}

[MessagePackObject]
public class S2C_LoginResult
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public long PlayerId { get; set; }

    [Key(3)]
    public string PlayerName { get; set; } = string.Empty;
}
