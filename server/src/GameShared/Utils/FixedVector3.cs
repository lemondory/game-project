using System;
using MessagePack;

namespace GameShared.Utils;

/// <summary>
/// 3D vector using Fixed32 for deterministic synchronization
/// Preferred for network-synced positions and physics
/// </summary>
[MessagePackObject]
public partial struct FixedVector3 : IEquatable<FixedVector3>
{
    [Key(0)] public Fixed32 X { get; set; }
    [Key(1)] public Fixed32 Y { get; set; }
    [Key(2)] public Fixed32 Z { get; set; }

    public FixedVector3(Fixed32 x, Fixed32 y, Fixed32 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public FixedVector3(float x, float y, float z)
    {
        X = Fixed32.FromFloat(x);
        Y = Fixed32.FromFloat(y);
        Z = Fixed32.FromFloat(z);
    }

    // Static constructors
    public static FixedVector3 FromFloats(float x, float y, float z)
        => new FixedVector3(x, y, z);

    public static FixedVector3 FromVector3(Vector3 vec)
        => new FixedVector3(vec.X, vec.Y, vec.Z);

    // Conversion to Vector3
    public Vector3 ToVector3()
        => new Vector3
        {
            X = X.ToFloat(),
            Y = Y.ToFloat(),
            Z = Z.ToFloat()
        };

    // Vector operations
    public static FixedVector3 operator +(FixedVector3 a, FixedVector3 b)
        => new FixedVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static FixedVector3 operator -(FixedVector3 a, FixedVector3 b)
        => new FixedVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static FixedVector3 operator *(FixedVector3 v, Fixed32 scalar)
        => new FixedVector3(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static FixedVector3 operator *(Fixed32 scalar, FixedVector3 v)
        => new FixedVector3(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static FixedVector3 operator /(FixedVector3 v, Fixed32 scalar)
        => new FixedVector3(v.X / scalar, v.Y / scalar, v.Z / scalar);

    public static FixedVector3 operator -(FixedVector3 v)
        => new FixedVector3(-v.X, -v.Y, -v.Z);

    // Comparison
    public static bool operator ==(FixedVector3 a, FixedVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(FixedVector3 a, FixedVector3 b)
        => !(a == b);

    // Magnitude and distance
    public Fixed32 SqrMagnitude()
        => X * X + Y * Y + Z * Z;

    public Fixed32 Magnitude()
        => Fixed32.Sqrt(SqrMagnitude());

    public Fixed32 Distance(FixedVector3 other)
    {
        Fixed32 dx = X - other.X;
        Fixed32 dy = Y - other.Y;
        Fixed32 dz = Z - other.Z;
        return Fixed32.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public Fixed32 SqrDistance(FixedVector3 other)
    {
        Fixed32 dx = X - other.X;
        Fixed32 dy = Y - other.Y;
        Fixed32 dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    // Normalization
    public FixedVector3 Normalized()
    {
        Fixed32 mag = Magnitude();
        if (mag == Fixed32.Zero)
            return Zero;
        return this / mag;
    }

    public void Normalize()
    {
        Fixed32 mag = Magnitude();
        if (mag != Fixed32.Zero)
        {
            X /= mag;
            Y /= mag;
            Z /= mag;
        }
    }

    // Dot product
    public static Fixed32 Dot(FixedVector3 a, FixedVector3 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    // Lerp
    public static FixedVector3 Lerp(FixedVector3 a, FixedVector3 b, Fixed32 t)
    {
        t = Fixed32.Clamp(t, Fixed32.Zero, Fixed32.One);
        return new FixedVector3(
            Fixed32.Lerp(a.X, b.X, t),
            Fixed32.Lerp(a.Y, b.Y, t),
            Fixed32.Lerp(a.Z, b.Z, t)
        );
    }

    // Constants
    public static readonly FixedVector3 Zero = new FixedVector3(Fixed32.Zero, Fixed32.Zero, Fixed32.Zero);
    public static readonly FixedVector3 One = new FixedVector3(Fixed32.One, Fixed32.One, Fixed32.One);
    public static readonly FixedVector3 Up = new FixedVector3(Fixed32.Zero, Fixed32.One, Fixed32.Zero);
    public static readonly FixedVector3 Down = new FixedVector3(Fixed32.Zero, Fixed32.MinusOne, Fixed32.Zero);
    public static readonly FixedVector3 Left = new FixedVector3(Fixed32.MinusOne, Fixed32.Zero, Fixed32.Zero);
    public static readonly FixedVector3 Right = new FixedVector3(Fixed32.One, Fixed32.Zero, Fixed32.Zero);
    public static readonly FixedVector3 Forward = new FixedVector3(Fixed32.Zero, Fixed32.Zero, Fixed32.One);
    public static readonly FixedVector3 Back = new FixedVector3(Fixed32.Zero, Fixed32.Zero, Fixed32.MinusOne);

    // IEquatable
    public bool Equals(FixedVector3 other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj)
        => obj is FixedVector3 other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    // String representation
    public override string ToString()
        => $"({X}, {Y}, {Z})";
}
