using GameServer.Network;

namespace GameServer.Tests;

/// <summary>
/// 서버가 클라이언트에서 수신한 패킷 데이터를 올바르게 검증하는지 확인한다.
/// 잘못된 좌표는 치트/DoS 방지를 위해 거부되어야 한다.
/// </summary>
public class PacketValidationTests
{
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(10000f, 0f, 0f)]
    [InlineData(-10000f, 5000f, -9999.9f)]
    [InlineData(1f, 2f, 3f)]
    public void IsValidCoordinate_ReturnsTrue_ForValidCoordinates(float x, float y, float z)
    {
        Assert.True(PacketHandler.IsValidCoordinate(x, y, z));
    }

    [Theory]
    [InlineData(10001f,                 0f,    0f)]   // X 범위 초과
    [InlineData(-10001f,                0f,    0f)]   // X 음수 범위 초과
    [InlineData(0f,                 10001f,    0f)]   // Y 범위 초과
    [InlineData(0f,                     0f, 10001f)]  // Z 범위 초과
    [InlineData(float.NaN,              0f,    0f)]   // NaN
    [InlineData(0f,             float.NaN,    0f)]
    [InlineData(0f,                     0f, float.NaN)]
    [InlineData(float.PositiveInfinity, 0f,    0f)]   // Infinity
    public void IsValidCoordinate_ReturnsFalse_ForInvalidCoordinates(float x, float y, float z)
    {
        Assert.False(PacketHandler.IsValidCoordinate(x, y, z));
    }
}
