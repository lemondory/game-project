using System.Collections.Generic;

namespace DataConverter.Models;

/// <summary>
/// Represents an enum definition from enums.xlsx
/// </summary>
public class EnumSchema
{
    /// <summary>Enum name (from sheet name or Name column)</summary>
    public string EnumName { get; set; } = string.Empty;

    /// <summary>Enum values</summary>
    public List<EnumValue> Values { get; set; } = new();
}

/// <summary>
/// Single enum value
/// </summary>
public class EnumValue
{
    /// <summary>Enum member name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Numeric value</summary>
    public int Value { get; set; }

    /// <summary>Description/comment</summary>
    public string Description { get; set; } = string.Empty;
}
