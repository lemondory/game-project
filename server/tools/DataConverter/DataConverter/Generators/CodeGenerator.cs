using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataConverter.Models;

namespace DataConverter.Generators;

/// <summary>
/// Generates C# classes from TableSchema
/// </summary>
public class CodeGenerator
{
    /// <summary>
    /// Generate both Data class and Table class in a single file
    /// </summary>
    public string GenerateCombinedDataClass(TableSchema schema, HashSet<string> enumNames)
    {
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using MessagePack;");
        sb.AppendLine("using GameShared.Utils;");

        // Add enum namespace if any column uses enum type
        bool hasEnumType = schema.Columns.Any(c => enumNames.Contains(c.TypeName));
        if (hasEnumType)
        {
            sb.AppendLine("using GameShared.Generated.Enums;");
        }

        sb.AppendLine();
        sb.AppendLine("namespace GameShared.Generated.Data;");
        sb.AppendLine();

        // ===== Data Class =====
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Auto-generated data class from {schema.TableName}.xlsx");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[MessagePackObject]");
        sb.AppendLine($"public partial class {schema.ClassName}");
        sb.AppendLine("{");

        // Generate properties (skip ignored columns)
        int keyIndex = 0;
        var regularColumns = schema.Columns
            .Where(c => !c.IsArray && !c.IsIgnored)
            .ToList();
        var arrayBaseNames = schema.ArrayColumns
            .Where(kvp => kvp.Value.All(c => !c.IsIgnored))
            .Select(kvp => kvp.Key)
            .ToList();

        // Regular properties
        foreach (var column in regularColumns)
        {
            if (!string.IsNullOrEmpty(column.Description))
            {
                sb.AppendLine($"    /// <summary>{column.Description}</summary>");
            }

            sb.AppendLine($"    [Key({keyIndex++})]");
            sb.AppendLine($"    public {column.CSharpType} {column.Name} {{ get; set; }}{GetDefaultValue(column)}");
            sb.AppendLine();
        }

        // Array properties
        foreach (var arrayBaseName in arrayBaseNames)
        {
            var arrayColumns = schema.ArrayColumns[arrayBaseName];
            var firstColumn = arrayColumns[0];

            string elementType = firstColumn.TypeName switch
            {
                "int" or "long" or "short" or "byte" => firstColumn.TypeName,
                "float" or "double" or "decimal" => firstColumn.TypeName,
                "fixed" => "Fixed32",
                "string" => "string",
                "bool" => "bool",
                _ when enumNames.Contains(firstColumn.TypeName) => firstColumn.TypeName,
                _ => "object"
            };

            if (firstColumn.Description != null)
            {
                sb.AppendLine($"    /// <summary>{firstColumn.Description}</summary>");
            }

            sb.AppendLine($"    [Key({keyIndex++})]");
            sb.AppendLine($"    public {elementType}[] {arrayBaseName} {{ get; set; }} = System.Array.Empty<{elementType}>();");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // ===== Table Class =====
        string pkeyType = schema.PrimaryKey?.CSharpType.TrimEnd('?') ?? "int";

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Auto-generated table class for {schema.ClassName}");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[MessagePackObject]");
        sb.AppendLine($"public partial class {schema.CollectionClassName} : GameShared.Data.IDataTable");
        sb.AppendLine("{");

        sb.AppendLine($"    [Key(0)]");
        sb.AppendLine($"    public Dictionary<{pkeyType}, {schema.ClassName}> Data {{ get; set; }} = new();");
        sb.AppendLine();

        // Group indexes (non-serialized)
        if (schema.GroupColumns.Count > 0)
        {
            foreach (var groupCol in schema.GroupColumns)
            {
                string groupKeyType = groupCol.CSharpType.TrimEnd('?');
                sb.AppendLine($"    [IgnoreMember]");
                sb.AppendLine($"    private Dictionary<{groupKeyType}, List<{schema.ClassName}>>? _{ToCamelCase(groupCol.Name)}Index;");
                sb.AppendLine();
            }
        }

        // Helper methods
        sb.AppendLine($"    public {schema.ClassName}? GetById({pkeyType} id) => Data.GetValueOrDefault(id);");
        sb.AppendLine();
        sb.AppendLine($"    public IEnumerable<{schema.ClassName}> GetAll() => Data.Values;");
        sb.AppendLine();
        sb.AppendLine($"    public IEnumerable<{schema.ClassName}> Where(Func<{schema.ClassName}, bool> predicate)");
        sb.AppendLine("        => Data.Values.Where(predicate);");
        sb.AppendLine();
        sb.AppendLine("    [IgnoreMember]");
        sb.AppendLine($"    public int Count => Data.Count;");

        // Group query methods
        if (schema.GroupColumns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Build indexes for group queries. Call this after loading data.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void BuildIndexes()");
            sb.AppendLine("    {");

            foreach (var groupCol in schema.GroupColumns)
            {
                string camelName = ToCamelCase(groupCol.Name);
                sb.AppendLine($"        _{camelName}Index = Data.Values");
                sb.AppendLine($"            .GroupBy(x => x.{groupCol.Name})");
                sb.AppendLine($"            .ToDictionary(g => g.Key, g => g.ToList());");
            }

            sb.AppendLine("    }");

            foreach (var groupCol in schema.GroupColumns)
            {
                string groupKeyType = groupCol.CSharpType.TrimEnd('?');
                string camelName = ToCamelCase(groupCol.Name);

                sb.AppendLine();
                sb.AppendLine($"    /// <summary>");
                sb.AppendLine($"    /// Get all records with the specified {groupCol.Name}");
                sb.AppendLine($"    /// </summary>");
                sb.AppendLine($"    public IReadOnlyList<{schema.ClassName}> GetBy{groupCol.Name}({groupKeyType} {camelName})");
                sb.AppendLine("    {");
                sb.AppendLine($"        if (_{camelName}Index == null)");
                sb.AppendLine($"            throw new InvalidOperationException(\"Indexes not built. Call BuildIndexes() first.\");");
                sb.AppendLine();
                sb.AppendLine($"        return _{camelName}Index.TryGetValue({camelName}, out var list)");
                sb.AppendLine($"            ? list");
                sb.AppendLine($"            : new List<{schema.ClassName}>();");
                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    public string GenerateDataClass(TableSchema schema, HashSet<string> enumNames)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using MessagePack;");
        sb.AppendLine("using GameShared.Utils;");
        sb.AppendLine();
        sb.AppendLine("namespace GameShared.Generated.Data;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Auto-generated data class from {schema.TableName}.xlsx");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[MessagePackObject]");
        sb.AppendLine($"public partial class {schema.ClassName}");
        sb.AppendLine("{");

        // Generate properties (skip ignored columns)
        int keyIndex = 0;
        var regularColumns = schema.Columns
            .Where(c => !c.IsArray && !c.IsIgnored)
            .ToList();
        var arrayBaseNames = schema.ArrayColumns
            .Where(kvp => kvp.Value.All(c => !c.IsIgnored))
            .Select(kvp => kvp.Key)
            .ToList();

        // Regular properties
        foreach (var column in regularColumns)
        {
            if (!string.IsNullOrEmpty(column.Description))
            {
                sb.AppendLine($"    /// <summary>{column.Description}</summary>");
            }

            sb.AppendLine($"    [Key({keyIndex++})]");
            sb.AppendLine($"    public {column.CSharpType} {column.Name} {{ get; set; }}{GetDefaultValue(column)}");
            sb.AppendLine();
        }

        // Array properties
        foreach (var arrayBaseName in arrayBaseNames)
        {
            var arrayColumns = schema.ArrayColumns[arrayBaseName];
            var firstColumn = arrayColumns[0];

            string elementType = firstColumn.TypeName switch
            {
                "int" or "long" or "short" or "byte" => firstColumn.TypeName,
                "float" or "double" or "decimal" => firstColumn.TypeName,
                "fixed" => "Fixed32",
                "string" => "string",
                "bool" => "bool",
                _ when enumNames.Contains(firstColumn.TypeName) => firstColumn.TypeName,
                _ => "object"
            };

            if (firstColumn.Description != null)
            {
                sb.AppendLine($"    /// <summary>{firstColumn.Description}</summary>");
            }

            sb.AppendLine($"    [Key({keyIndex++})]");
            sb.AppendLine($"    public {elementType}[] {arrayBaseName} {{ get; set; }} = System.Array.Empty<{elementType}>();");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    public string GenerateTableClass(TableSchema schema)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using MessagePack;");
        sb.AppendLine();
        sb.AppendLine("namespace GameShared.Generated.Data;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Auto-generated table class for {schema.ClassName}");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[MessagePackObject]");
        sb.AppendLine($"public partial class {schema.CollectionClassName} : GameShared.Data.IDataTable");
        sb.AppendLine("{");

        // Primary key type
        string keyType = schema.PrimaryKey?.CSharpType.TrimEnd('?') ?? "int";

        sb.AppendLine($"    [Key(0)]");
        sb.AppendLine($"    public Dictionary<{keyType}, {schema.ClassName}> Data {{ get; set; }} = new();");
        sb.AppendLine();

        // Group indexes (non-serialized)
        if (schema.GroupColumns.Count > 0)
        {
            foreach (var groupCol in schema.GroupColumns)
            {
                string groupKeyType = groupCol.CSharpType.TrimEnd('?');
                sb.AppendLine($"    [IgnoreMember]");
                sb.AppendLine($"    private Dictionary<{groupKeyType}, List<{schema.ClassName}>>? _{ToCamelCase(groupCol.Name)}Index;");
                sb.AppendLine();
            }
        }

        // Helper methods
        sb.AppendLine($"    public {schema.ClassName}? GetById({keyType} id) => Data.GetValueOrDefault(id);");
        sb.AppendLine();
        sb.AppendLine($"    public IEnumerable<{schema.ClassName}> GetAll() => Data.Values;");
        sb.AppendLine();
        sb.AppendLine($"    public IEnumerable<{schema.ClassName}> Where(Func<{schema.ClassName}, bool> predicate)");
        sb.AppendLine("        => Data.Values.Where(predicate);");
        sb.AppendLine();
        sb.AppendLine("    [IgnoreMember]");
        sb.AppendLine($"    public int Count => Data.Count;");

        // Group query methods
        if (schema.GroupColumns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Build indexes for group queries. Call this after loading data.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void BuildIndexes()");
            sb.AppendLine("    {");

            foreach (var groupCol in schema.GroupColumns)
            {
                string camelName = ToCamelCase(groupCol.Name);
                sb.AppendLine($"        _{camelName}Index = Data.Values");
                sb.AppendLine($"            .GroupBy(x => x.{groupCol.Name})");
                sb.AppendLine($"            .ToDictionary(g => g.Key, g => g.ToList());");
            }

            sb.AppendLine("    }");

            foreach (var groupCol in schema.GroupColumns)
            {
                string groupKeyType = groupCol.CSharpType.TrimEnd('?');
                string camelName = ToCamelCase(groupCol.Name);

                sb.AppendLine();
                sb.AppendLine($"    /// <summary>");
                sb.AppendLine($"    /// Get all records with the specified {groupCol.Name}");
                sb.AppendLine($"    /// </summary>");
                sb.AppendLine($"    public IReadOnlyList<{schema.ClassName}> GetBy{groupCol.Name}({groupKeyType} {camelName})");
                sb.AppendLine("    {");
                sb.AppendLine($"        if (_{camelName}Index == null)");
                sb.AppendLine($"            throw new InvalidOperationException(\"Indexes not built. Call BuildIndexes() first.\");");
                sb.AppendLine();
                sb.AppendLine($"        return _{camelName}Index.TryGetValue({camelName}, out var list)");
                sb.AppendLine($"            ? list");
                sb.AppendLine($"            : new List<{schema.ClassName}>();");
                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generate all enums in a single file
    /// </summary>
    public string GenerateAllEnumsCode(List<EnumSchema> enumSchemas)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// Auto-generated file by DataConverter");
        sb.AppendLine("// DO NOT EDIT MANUALLY");
        sb.AppendLine();
        sb.AppendLine("namespace GameShared.Generated.Enums;");
        sb.AppendLine();

        for (int enumIndex = 0; enumIndex < enumSchemas.Count; enumIndex++)
        {
            var enumSchema = enumSchemas[enumIndex];

            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Auto-generated enum from enums.xlsx");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public enum {enumSchema.EnumName}");
            sb.AppendLine("{");

            // Add None = -1 if not already present
            bool hasNone = enumSchema.Values.Any(v => v.Name == "None");
            if (!hasNone)
            {
                sb.AppendLine("    /// <summary>사용 안함</summary>");
                sb.AppendLine("    None = -1,");
                sb.AppendLine();
            }

            for (int i = 0; i < enumSchema.Values.Count; i++)
            {
                var value = enumSchema.Values[i];

                if (!string.IsNullOrWhiteSpace(value.Description))
                {
                    sb.AppendLine($"    /// <summary>{value.Description}</summary>");
                }

                sb.Append($"    {value.Name} = {value.Value}");
                sb.AppendLine(",");
            }

            // Add Max value for loop support
            var maxValue = enumSchema.Values.Max(v => v.Value) + 1;
            sb.AppendLine();
            sb.AppendLine("    /// <summary>최대값 (for loop 용)</summary>");
            sb.AppendLine($"    Max = {maxValue}");

            sb.AppendLine("}");

            // Add blank line between enums (except for the last one)
            if (enumIndex < enumSchemas.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate single enum code (kept for backwards compatibility)
    /// </summary>
    public string GenerateEnumCode(EnumSchema enumSchema)
    {
        var sb = new StringBuilder();

        sb.AppendLine("namespace GameShared.Generated.Enums;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Auto-generated enum from enums.xlsx");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public enum {enumSchema.EnumName}");
        sb.AppendLine("{");

        // Add None = -1 if not already present
        bool hasNone = enumSchema.Values.Any(v => v.Name == "None");
        if (!hasNone)
        {
            sb.AppendLine("    /// <summary>사용 안함</summary>");
            sb.AppendLine("    None = -1,");
            sb.AppendLine();
        }

        for (int i = 0; i < enumSchema.Values.Count; i++)
        {
            var value = enumSchema.Values[i];

            if (!string.IsNullOrWhiteSpace(value.Description))
            {
                sb.AppendLine($"    /// <summary>{value.Description}</summary>");
            }

            sb.Append($"    {value.Name} = {value.Value}");
            sb.AppendLine(",");
        }

        // Add Max value for loop support
        var maxValue = enumSchema.Values.Max(v => v.Value) + 1;
        sb.AppendLine();
        sb.AppendLine("    /// <summary>최대값 (for loop 용)</summary>");
        sb.AppendLine($"    Max = {maxValue}");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GetDefaultValue(ColumnInfo column)
    {
        if (column.IsNullable)
            return string.Empty;

        return column.TypeName switch
        {
            "string" => " = string.Empty;",
            _ => string.Empty
        };
    }

    private string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        return char.ToLower(pascalCase[0]) + pascalCase.Substring(1);
    }

    public string GenerateConstClass(ConstSchema constSchema)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using GameShared.Utils;");
        sb.AppendLine();
        sb.AppendLine("namespace GameShared.Generated.Data;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated constants from Consts.xlsx");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class GameConsts");
        sb.AppendLine("{");

        foreach (var constValue in constSchema.Values.OrderBy(c => c.Id))
        {
            if (!string.IsNullOrWhiteSpace(constValue.Description))
            {
                sb.AppendLine($"    /// <summary>{constValue.Description}</summary>");
            }

            string csharpType = GetConstCSharpType(constValue.ValueType);
            string formattedValue = FormatConstValue(constValue.Value, constValue.ValueType);

            // Use const for primitive types, readonly for others
            bool isConst = IsConstCompatible(constValue.ValueType);

            if (isConst)
            {
                sb.AppendLine($"    public const {csharpType} {constValue.Name} = {formattedValue};");
            }
            else
            {
                sb.AppendLine($"    public static readonly {csharpType} {constValue.Name} = {formattedValue};");
            }

            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GetConstCSharpType(string valueType)
    {
        return valueType.ToLower() switch
        {
            "int" or "int32" => "int",
            "long" or "int64" => "long",
            "short" or "int16" => "short",
            "byte" => "byte",
            "float" or "single" => "float",
            "double" => "double",
            "decimal" => "decimal",
            "fixed" or "fixed32" => "Fixed32",
            "string" => "string",
            "bool" or "boolean" => "bool",
            _ => "object"
        };
    }

    private string FormatConstValue(string value, string valueType)
    {
        valueType = valueType.ToLower();

        return valueType switch
        {
            "int" or "int32" or "long" or "int64" or "short" or "int16" or "byte" => value,
            "float" or "single" => value.Contains('.') ? $"{value}f" : $"{value}.0f",
            "double" => value.Contains('.') ? $"{value}d" : $"{value}.0d",
            "decimal" => value.Contains('.') ? $"{value}m" : $"{value}.0m",
            "fixed" or "fixed32" => $"Fixed32.FromFloat({value}f)",
            "string" => $"\"{value.Replace("\"", "\\\"")}\"",
            "bool" or "boolean" => value.ToLower() == "true" || value == "1" ? "true" : "false",
            _ => $"\"{value}\""
        };
    }

    private bool IsConstCompatible(string valueType)
    {
        // const can only be used with primitive types
        valueType = valueType.ToLower();
        return valueType switch
        {
            "int" or "int32" or "long" or "int64" or "short" or "int16" or "byte" => true,
            "float" or "single" or "double" or "decimal" => true,
            "string" => true,
            "bool" or "boolean" => true,
            _ => false // fixed, objects, etc. need readonly
        };
    }

    public string GenerateDataManagerClass(List<TableSchema> schemas)
    {
        var sb = new StringBuilder();

        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("// Auto-generated file by DataConverter");
        sb.AppendLine("// DO NOT EDIT MANUALLY");
        sb.AppendLine();
        sb.AppendLine("using GameShared.Generated.Data;");
        sb.AppendLine();
        sb.AppendLine("namespace GameShared.Data;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated GameDataManager class");
        sb.AppendLine("/// All data tables are automatically registered");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class GameDataManager : DataManagerBase");
        sb.AppendLine("{");

        // Add singleton pattern
        sb.AppendLine("    private static GameDataManager? _instance;");
        sb.AppendLine("    private static readonly object _lock = new object();");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Singleton instance</summary>");
        sb.AppendLine("    public static GameDataManager Instance");
        sb.AppendLine("    {");
        sb.AppendLine("        get");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_instance == null)");
        sb.AppendLine("            {");
        sb.AppendLine("                lock (_lock)");
        sb.AppendLine("                {");
        sb.AppendLine("                    _instance ??= new GameDataManager();");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            return _instance;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private GameDataManager() : base()");
        sb.AppendLine("    {");
        sb.AppendLine("        // Private constructor for singleton");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Private fields
        foreach (var schema in schemas.OrderBy(s => s.TableName))
        {
            sb.AppendLine($"    private {schema.CollectionClassName} _{ToCamelCase(schema.TableName)} = new();");
        }

        sb.AppendLine();

        // Public static properties (for easy access without .Instance)
        foreach (var schema in schemas.OrderBy(s => s.TableName))
        {
            sb.AppendLine($"    /// <summary>Access to {schema.TableName}</summary>");
            sb.AppendLine($"    public static {schema.CollectionClassName} {schema.TableName} => Instance._{ToCamelCase(schema.TableName)};");
            sb.AppendLine();
        }

        // LoadAllTables implementation
        sb.AppendLine("    protected override void LoadAllTables()");
        sb.AppendLine("    {");

        foreach (var schema in schemas.OrderBy(s => s.TableName))
        {
            sb.AppendLine($"        _{ToCamelCase(schema.TableName)} = LoadTable<{schema.CollectionClassName}>(\"{schema.TableName}.bytes\");");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // ClearAllTables implementation
        sb.AppendLine("    protected override void ClearAllTables()");
        sb.AppendLine("    {");

        foreach (var schema in schemas.OrderBy(s => s.TableName))
        {
            sb.AppendLine($"        _{ToCamelCase(schema.TableName)} = new {schema.CollectionClassName}();");
        }

        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }
}
