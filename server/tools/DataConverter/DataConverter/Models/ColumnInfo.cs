namespace DataConverter.Models;

/// <summary>
/// Represents column metadata extracted from XLSX
/// </summary>
public class ColumnInfo
{
    /// <summary>Column name (Row 1, PascalCase)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Data type (Row 2)</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Description (Row 3, optional)</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Column index in Excel</summary>
    public int ColumnIndex { get; set; }

    /// <summary>Is this column part of an array?</summary>
    public bool IsArray { get; set; }

    /// <summary>Base name for array columns (without [] suffix)</summary>
    public string? ArrayBaseName { get; set; }

    /// <summary>Array element index (for multi-column arrays)</summary>
    public int ArrayIndex { get; set; }

    /// <summary>Is this a reference type (foreign key)?</summary>
    public bool IsReference { get; set; }

    /// <summary>Referenced table name (for ref:TableName)</summary>
    public string? ReferenceTable { get; set; }

    /// <summary>Is this type nullable?</summary>
    public bool IsNullable { get; set; }

    /// <summary>Is this a group key column? (e.g., GroupId, CategoryGroup)</summary>
    public bool IsGroupKey { get; set; }

    /// <summary>Is this column ignored? (TypeName = "ignore" for design-only columns)</summary>
    public bool IsIgnored { get; set; }

    /// <summary>C# type representation</summary>
    public string CSharpType
    {
        get
        {
            string baseType = TypeName switch
            {
                "int" or "long" or "short" or "byte" => TypeName,
                "float" or "double" or "decimal" => TypeName,
                "fixed" => "Fixed32",
                "string" => "string",
                "bool" => "bool",
                "DateTime" => "DateTime",
                var t when t.StartsWith("ref:") => "int", // FK는 int로 저장
                _ when IsEnumType() => TypeName.TrimEnd('?'),
                _ => "object"
            };

            if (IsArray)
            {
                return $"{baseType}[]";
            }

            if (IsNullable && baseType != "string")
            {
                return $"{baseType}?";
            }

            return baseType;
        }
    }

    private bool IsEnumType()
    {
        // Enum types start with uppercase (MonsterType, ItemRarity, etc.)
        return !string.IsNullOrEmpty(TypeName) &&
               char.IsUpper(TypeName[0]) &&
               !TypeName.StartsWith("ref:");
    }
}
