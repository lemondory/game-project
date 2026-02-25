using System;
using MessagePack;

namespace GameShared.Utils;

/// <summary>
/// Fixed-point number with 4 decimal places (scale: 10000)
/// Range: ±214,748.3648
/// Thread-safe, deterministic across platforms
/// </summary>
[MessagePackObject]
public partial struct Fixed32 : IEquatable<Fixed32>, IComparable<Fixed32>
{
    private const int Scale = 10000;
    private const float ScaleF = 10000f;

    [Key(0)]
    public int RawValue { get; private set; }

    private Fixed32(int rawValue)
    {
        RawValue = rawValue;
    }

    // Conversion from primitives
    public static Fixed32 FromFloat(float value) => new Fixed32((int)(value * ScaleF));
    public static Fixed32 FromDouble(double value) => new Fixed32((int)(value * Scale));
    public static Fixed32 FromInt(int value) => new Fixed32(value * Scale);
    public static Fixed32 FromRaw(int rawValue) => new Fixed32(rawValue);

    // Conversion to primitives
    public float ToFloat() => (float)RawValue / ScaleF;
    public double ToDouble() => (double)RawValue / Scale;
    public int ToInt() => RawValue / Scale;

    // Arithmetic operators
    public static Fixed32 operator +(Fixed32 a, Fixed32 b)
        => new Fixed32(a.RawValue + b.RawValue);

    public static Fixed32 operator -(Fixed32 a, Fixed32 b)
        => new Fixed32(a.RawValue - b.RawValue);

    public static Fixed32 operator *(Fixed32 a, Fixed32 b)
    {
        long result = (long)a.RawValue * b.RawValue / Scale;
        return new Fixed32((int)result);
    }

    public static Fixed32 operator /(Fixed32 a, Fixed32 b)
    {
        if (b.RawValue == 0)
            throw new DivideByZeroException("Cannot divide by zero");

        long result = (long)a.RawValue * Scale / b.RawValue;
        return new Fixed32((int)result);
    }

    public static Fixed32 operator -(Fixed32 a)
        => new Fixed32(-a.RawValue);

    // Comparison operators
    public static bool operator ==(Fixed32 a, Fixed32 b) => a.RawValue == b.RawValue;
    public static bool operator !=(Fixed32 a, Fixed32 b) => a.RawValue != b.RawValue;
    public static bool operator >(Fixed32 a, Fixed32 b) => a.RawValue > b.RawValue;
    public static bool operator <(Fixed32 a, Fixed32 b) => a.RawValue < b.RawValue;
    public static bool operator >=(Fixed32 a, Fixed32 b) => a.RawValue >= b.RawValue;
    public static bool operator <=(Fixed32 a, Fixed32 b) => a.RawValue <= b.RawValue;

    // IEquatable implementation
    public bool Equals(Fixed32 other) => RawValue == other.RawValue;
    public override bool Equals(object? obj) => obj is Fixed32 other && Equals(other);
    public override int GetHashCode() => RawValue.GetHashCode();

    // IComparable implementation
    public int CompareTo(Fixed32 other) => RawValue.CompareTo(other.RawValue);

    // Math functions
    public static Fixed32 Abs(Fixed32 value)
        => new Fixed32(Math.Abs(value.RawValue));

    public static Fixed32 Min(Fixed32 a, Fixed32 b)
        => new Fixed32(Math.Min(a.RawValue, b.RawValue));

    public static Fixed32 Max(Fixed32 a, Fixed32 b)
        => new Fixed32(Math.Max(a.RawValue, b.RawValue));

    public static Fixed32 Clamp(Fixed32 value, Fixed32 min, Fixed32 max)
    {
        if (value.RawValue < min.RawValue) return min;
        if (value.RawValue > max.RawValue) return max;
        return value;
    }

    public static Fixed32 Sqrt(Fixed32 value)
    {
        if (value.RawValue < 0)
            throw new ArgumentException("Cannot take square root of negative number");

        // Newton-Raphson method for integer square root
        if (value.RawValue == 0) return Zero;

        long scaledValue = (long)value.RawValue * Scale;
        long guess = scaledValue;
        long lastGuess;

        do
        {
            lastGuess = guess;
            guess = (guess + scaledValue / guess) / 2;
        } while (Math.Abs(guess - lastGuess) > 1);

        return new Fixed32((int)guess);
    }

    public static Fixed32 Lerp(Fixed32 a, Fixed32 b, Fixed32 t)
    {
        // Clamp t between 0 and 1
        t = Clamp(t, Zero, One);
        return a + (b - a) * t;
    }

    // Constants
    public static readonly Fixed32 Zero = new Fixed32(0);
    public static readonly Fixed32 One = new Fixed32(Scale);
    public static readonly Fixed32 MinusOne = new Fixed32(-Scale);
    public static readonly Fixed32 MaxValue = new Fixed32(int.MaxValue);
    public static readonly Fixed32 MinValue = new Fixed32(int.MinValue);

    // String representation
    public override string ToString()
    {
        int intPart = RawValue / Scale;
        int fracPart = Math.Abs(RawValue % Scale);
        return $"{intPart}.{fracPart:D4}";
    }

    // Parse from string
    public static Fixed32 Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("String cannot be null or whitespace");

        var parts = s.Trim().Split('.');
        if (parts.Length > 2)
            throw new FormatException("Invalid fixed-point format");

        int intPart = int.Parse(parts[0]);
        int fracPart = 0;

        if (parts.Length == 2)
        {
            // Pad or truncate to 4 digits
            string fracStr = parts[1].PadRight(4, '0').Substring(0, 4);
            fracPart = int.Parse(fracStr);
        }

        int rawValue = intPart * Scale + (intPart < 0 ? -fracPart : fracPart);
        return new Fixed32(rawValue);
    }

    public static bool TryParse(string s, out Fixed32 result)
    {
        try
        {
            result = Parse(s);
            return true;
        }
        catch
        {
            result = Zero;
            return false;
        }
    }

    // Implicit/Explicit conversions
    public static implicit operator Fixed32(int value) => FromInt(value);
    public static explicit operator Fixed32(float value) => FromFloat(value);
    public static explicit operator Fixed32(double value) => FromDouble(value);
    public static explicit operator float(Fixed32 value) => value.ToFloat();
    public static explicit operator double(Fixed32 value) => value.ToDouble();
    public static explicit operator int(Fixed32 value) => value.ToInt();
}
