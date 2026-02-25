using Google.Protobuf;
using GameShared.Proto;

namespace GameServer.Tests;

/// <summary>
/// Protobuf 패킷 직렬화/역직렬화가 정확하게 동작하는지 확인한다.
/// 네트워크를 통해 전송되는 패킷 데이터가 손실 없이 복원되어야 한다.
/// </summary>
public class PacketSerializationTests
{
    [Fact]
    public void C2S_Move_SerializesAndDeserializesCorrectly()
    {
        var original = new C2S_Move
        {
            Destination = new Vec3 { X = 5.5f, Y = 0f, Z = -3.2f }
        };

        var bytes  = original.ToByteArray();
        var parsed = C2S_Move.Parser.ParseFrom(bytes);

        Assert.Equal(original.Destination.X, parsed.Destination.X, precision: 4);
        Assert.Equal(original.Destination.Y, parsed.Destination.Y, precision: 4);
        Assert.Equal(original.Destination.Z, parsed.Destination.Z, precision: 4);
    }

    [Fact]
    public void S2C_LoginResult_Success_SerializesCorrectly()
    {
        var original = new S2C_LoginResult
        {
            Success    = true,
            Message    = "Login successful",
            PlayerId   = 42,
            PlayerName = "TestUser"
        };

        var bytes  = original.ToByteArray();
        var parsed = S2C_LoginResult.Parser.ParseFrom(bytes);

        Assert.True(parsed.Success);
        Assert.Equal(original.Message,    parsed.Message);
        Assert.Equal(original.PlayerId,   parsed.PlayerId);
        Assert.Equal(original.PlayerName, parsed.PlayerName);
    }

    [Fact]
    public void S2C_LoginResult_Failure_SerializesCorrectly()
    {
        var original = new S2C_LoginResult
        {
            Success = false,
            Message = "Invalid username or password"
        };

        var bytes  = original.ToByteArray();
        var parsed = S2C_LoginResult.Parser.ParseFrom(bytes);

        Assert.False(parsed.Success);
        Assert.Equal(original.Message, parsed.Message);
        Assert.Equal(0, parsed.PlayerId); // proto3 기본값
    }

    [Fact]
    public void C2S_Chat_SerializesAndDeserializesCorrectly()
    {
        var original = new C2S_Chat { Message = "Hello, World!" };

        var bytes  = original.ToByteArray();
        var parsed = C2S_Chat.Parser.ParseFrom(bytes);

        Assert.Equal(original.Message, parsed.Message);
    }
}
