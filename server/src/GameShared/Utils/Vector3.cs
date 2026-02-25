using System;
using MessagePack;

namespace GameShared.Utils;

[MessagePackObject]
public struct Vector3
{
    [Key(0)]
    public float X { get; set; }

    [Key(1)]
    public float Y { get; set; }

    [Key(2)]
    public float Z { get; set; }

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float Distance(Vector3 other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static Vector3 Zero => new(0, 0, 0);

    public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
}
