using System.Collections.Generic;

namespace DataConverter.Models;

/// <summary>
/// Represents a constant definition from Consts.xlsx
/// </summary>
public class ConstSchema
{
    /// <summary>All constants grouped by category (if using EnumName column)</summary>
    public List<ConstValue> Values { get; set; } = new();
}

/// <summary>
/// Single constant value
/// </summary>
public class ConstValue
{
    /// <summary>Unique ID</summary>
    public int Id { get; set; }

    /// <summary>Constant name (will be C# property name)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Value type (int, long, string, fixed, etc.)</summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>String representation of the value</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Description/comment</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Category/group name (optional, for organization)</summary>
    public string? Category { get; set; }
}
