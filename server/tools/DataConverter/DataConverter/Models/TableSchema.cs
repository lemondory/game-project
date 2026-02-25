using System.Collections.Generic;

namespace DataConverter.Models;

/// <summary>
/// Represents a complete table schema from XLSX
/// </summary>
public class TableSchema
{
    /// <summary>Table name (from file name, e.g., MonsterData)</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>Class name for the data row (e.g., MonsterData)</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Collection class name (e.g., MonsterDataTable)</summary>
    public string CollectionClassName { get; set; } = string.Empty;

    /// <summary>Primary key column (typically first column)</summary>
    public ColumnInfo? PrimaryKey { get; set; }

    /// <summary>All columns in the table</summary>
    public List<ColumnInfo> Columns { get; set; } = new();

    /// <summary>All data rows</summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>Source XLSX file path</summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>Array columns grouped by base name</summary>
    public Dictionary<string, List<ColumnInfo>> ArrayColumns { get; set; } = new();

    /// <summary>Group key columns (for GetByGroupId support)</summary>
    public List<ColumnInfo> GroupColumns { get; set; } = new();
}
