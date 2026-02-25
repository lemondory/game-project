using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataConverter.Models;
using ExcelDataReader;

namespace DataConverter.Parsers;

/// <summary>
/// Parses XLSX files into TableSchema
/// </summary>
public class ExcelParser
{
    static ExcelParser()
    {
        // Required for ExcelDataReader to work properly
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Opens a file with retry logic to handle Excel file locks
    /// </summary>
    private Stream OpenFileWithRetry(string filePath)
    {
        try
        {
            // Try to open directly with read-only sharing mode
            return File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (IOException)
        {
            // If file is locked, copy to temp file and open that instead
            var tempFile = Path.GetTempFileName();
            File.Copy(filePath, tempFile, overwrite: true);

            // Open temp file with auto-delete flag
            return new FileStream(tempFile, FileMode.Open, FileAccess.Read,
                FileShare.None, 4096, FileOptions.DeleteOnClose);
        }
    }

    public TableSchema ParseDataFile(string filePath)
    {
        using var stream = OpenFileWithRetry(filePath);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var schema = new TableSchema
        {
            SourceFilePath = filePath,
            TableName = Path.GetFileNameWithoutExtension(filePath),
            ClassName = Path.GetFileNameWithoutExtension(filePath),
            CollectionClassName = Path.GetFileNameWithoutExtension(filePath) + "Table"
        };

        // Read all data into DataSet
        var result = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false // We'll manually parse headers
            }
        });

        if (result.Tables.Count == 0)
            throw new InvalidOperationException($"No sheets found in {filePath}");

        var table = result.Tables[0]; // Use first sheet

        if (table.Rows.Count < 4)
            throw new InvalidOperationException($"File must have at least 4 rows (header, type, description, data)");

        // Parse columns from rows 1-3
        ParseColumns(table, schema);

        // Parse data rows (row 4+)
        ParseDataRows(table, schema);

        // Group array columns
        GroupArrayColumns(schema);

        return schema;
    }

    private void ParseColumns(System.Data.DataTable table, TableSchema schema)
    {
        var headerRow = table.Rows[0];
        var typeRow = table.Rows[1];
        var descRow = table.Rows[2];

        var arrayColumnCount = new Dictionary<string, int>();

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var columnName = headerRow[i]?.ToString()?.Trim();
            var typeName = typeRow[i]?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(typeName))
                break; // Stop at first empty column

            var description = descRow[i]?.ToString()?.Trim() ?? string.Empty;

            var column = new ColumnInfo
            {
                ColumnIndex = i,
                Name = columnName,
                TypeName = typeName,
                Description = description
            };

            // Check for ignored columns (design-only, using "ignore" type)
            if (typeName.Equals("ignore", StringComparison.OrdinalIgnoreCase))
            {
                column.IsIgnored = true;
            }

            // Check for array notation (ColumnName[])
            if (columnName.EndsWith("[]"))
            {
                column.IsArray = true;
                column.ArrayBaseName = columnName.Substring(0, columnName.Length - 2);

                // Track array index for multi-column arrays
                if (!arrayColumnCount.ContainsKey(column.ArrayBaseName))
                    arrayColumnCount[column.ArrayBaseName] = 0;

                column.ArrayIndex = arrayColumnCount[column.ArrayBaseName]++;
                column.Name = column.ArrayBaseName; // Remove [] from actual property name
            }

            // Check for reference type (ref:TableName)
            if (typeName.StartsWith("ref:"))
            {
                column.IsReference = true;
                column.ReferenceTable = typeName.Substring(4);
                column.TypeName = "int"; // References are stored as int IDs
            }

            // Check for nullable type (TypeName?)
            if (typeName.EndsWith("?"))
            {
                column.IsNullable = true;
                column.TypeName = typeName.TrimEnd('?');
            }

            // Check for group key pattern (GroupId, CategoryGroup, etc.)
            if (columnName.EndsWith("GroupId") || columnName.EndsWith("Group") || columnName.Contains("Group"))
            {
                column.IsGroupKey = true;
                schema.GroupColumns.Add(column);
            }

            schema.Columns.Add(column);
        }

        // Set primary key (first column by convention)
        if (schema.Columns.Count > 0)
            schema.PrimaryKey = schema.Columns[0];
    }

    private void ParseDataRows(System.Data.DataTable table, TableSchema schema)
    {
        for (int rowIndex = 3; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];

            // Check if row is commented out (first column starts with #)
            var firstCellValue = row[0]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstCellValue) && firstCellValue.StartsWith("#"))
            {
                continue; // Skip commented row
            }

            var dataRow = new Dictionary<string, object?>();
            bool isEmptyRow = true;

            for (int colIndex = 0; colIndex < schema.Columns.Count; colIndex++)
            {
                var column = schema.Columns[colIndex];
                var cellValue = row[column.ColumnIndex];

                if (cellValue != null && !string.IsNullOrWhiteSpace(cellValue.ToString()))
                    isEmptyRow = false;

                var parsedValue = ParseCellValue(cellValue, column);

                // For array columns, use full key with index
                string key = column.IsArray ? $"{column.Name}_{column.ArrayIndex}" : column.Name;
                dataRow[key] = parsedValue;
            }

            if (!isEmptyRow)
                schema.Rows.Add(dataRow);
        }
    }

    private object? ParseCellValue(object? cellValue, ColumnInfo column)
    {
        if (cellValue == null || string.IsNullOrWhiteSpace(cellValue.ToString()))
        {
            if (column.IsNullable)
                return null;

            // Return default values for non-nullable types
            return column.TypeName switch
            {
                "int" or "long" or "short" or "byte" => 0,
                "float" or "double" or "decimal" or "fixed" => 0.0,
                "bool" => false,
                "string" => string.Empty,
                _ => null
            };
        }

        try
        {
            // For numeric types, try to use the native double value from Excel
            // to avoid precision loss from string conversion
            if (cellValue is double doubleValue)
            {
                return column.TypeName switch
                {
                    "int" => (int)doubleValue,
                    "long" => (long)doubleValue,
                    "short" => (short)doubleValue,
                    "byte" => (byte)doubleValue,
                    "float" => (float)doubleValue,
                    "double" => doubleValue,
                    "decimal" => (decimal)doubleValue,
                    "fixed" => doubleValue, // Keep as double for precision, convert to Fixed32 later
                    _ => cellValue.ToString()!.Trim()
                };
            }

            // Fallback to string parsing for non-numeric cells
            string strValue = cellValue.ToString()!.Trim();

            return column.TypeName switch
            {
                "int" => int.Parse(strValue),
                "long" => long.Parse(strValue),
                "short" => short.Parse(strValue),
                "byte" => byte.Parse(strValue),
                "float" => float.Parse(strValue),
                "double" => double.Parse(strValue),
                "decimal" => decimal.Parse(strValue),
                "fixed" => double.Parse(strValue), // Parse as double for precision
                "bool" => ParseBool(strValue),
                "DateTime" => DateTime.Parse(strValue),
                "string" => strValue,
                _ => strValue // Enum or unknown types stored as string
            };
        }
        catch (Exception ex)
        {
            throw new FormatException($"Failed to parse '{cellValue}' as {column.TypeName} for column {column.Name}: {ex.Message}");
        }
    }

    private bool ParseBool(string value)
    {
        value = value.ToLower();
        return value == "true" || value == "1" || value == "yes";
    }

    private void GroupArrayColumns(TableSchema schema)
    {
        var arrayGroups = schema.Columns
            .Where(c => c.IsArray)
            .GroupBy(c => c.ArrayBaseName!);

        foreach (var group in arrayGroups)
        {
            schema.ArrayColumns[group.Key] = group.OrderBy(c => c.ArrayIndex).ToList();
        }
    }

    public List<EnumSchema> ParseEnumFile(string filePath)
    {
        var enums = new List<EnumSchema>();

        using var stream = OpenFileWithRetry(filePath);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var result = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false
            }
        });

        // Process all sheets
        foreach (System.Data.DataTable table in result.Tables)
        {
            // Check if this sheet uses the new format (EnumName column)
            bool hasEnumNameColumn = false;
            if (table.Rows.Count > 0)
            {
                var firstCell = table.Rows[0][0]?.ToString()?.Trim();
                if (firstCell == "EnumName")
                {
                    hasEnumNameColumn = true;
                }
            }

            if (hasEnumNameColumn)
            {
                // New format: Single sheet with EnumName column
                ParseEnumsWithGrouping(table, enums);
            }
            else
            {
                // Legacy format: Each sheet is one enum
                ParseEnumFromSheet(table, enums);
            }
        }

        return enums;
    }

    private void ParseEnumsWithGrouping(System.Data.DataTable table, List<EnumSchema> enums)
    {
        // New format:
        // Row N:   EnumName | MonsterType
        // Row N+1: Name | Value | Description  (header, skip)
        // Row N+2: Normal | 0 | 일반 몬스터

        var enumGroups = new Dictionary<string, EnumSchema>();
        string? currentEnumName = null;

        for (int i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var firstCell = row[0]?.ToString()?.Trim();

            // Check if this is an EnumName declaration row
            if (firstCell == "EnumName")
            {
                currentEnumName = row[1]?.ToString()?.Trim();

                if (!string.IsNullOrWhiteSpace(currentEnumName))
                {
                    enumGroups[currentEnumName] = new EnumSchema { EnumName = currentEnumName };
                }

                // Skip next row (header row: Name | Value | Description)
                i++;
                continue;
            }

            // Skip empty rows
            if (string.IsNullOrWhiteSpace(firstCell))
            {
                currentEnumName = null; // Reset current enum on empty row
                continue;
            }

            // Parse enum value (only if we have a current enum)
            if (currentEnumName != null && enumGroups.ContainsKey(currentEnumName))
            {
                var name = row[0]?.ToString()?.Trim();
                var valueStr = row[1]?.ToString()?.Trim();
                var description = table.Columns.Count > 2 ? row[2]?.ToString()?.Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!int.TryParse(valueStr, out int value))
                    throw new FormatException($"Invalid enum value '{valueStr}' for {currentEnumName}.{name}");

                enumGroups[currentEnumName].Values.Add(new EnumValue
                {
                    Name = name,
                    Value = value,
                    Description = description ?? string.Empty
                });
            }
        }

        enums.AddRange(enumGroups.Values);
    }

    private void ParseEnumFromSheet(System.Data.DataTable table, List<EnumSchema> enums)
    {
        // Legacy format: Sheet name is enum name
        var enumSchema = new EnumSchema
        {
            EnumName = table.TableName
        };

        // Skip header row (row 0), type row (row 1), description row (row 2)
        for (int i = 3; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];

            var name = row[0]?.ToString()?.Trim();
            var valueStr = row[1]?.ToString()?.Trim();
            var description = table.Columns.Count > 2 ? row[2]?.ToString()?.Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
                break;

            if (!int.TryParse(valueStr, out int value))
                throw new FormatException($"Invalid enum value '{valueStr}' for {name} in {table.TableName}");

            enumSchema.Values.Add(new EnumValue
            {
                Name = name,
                Value = value,
                Description = description ?? string.Empty
            });
        }

        if (enumSchema.Values.Count > 0)
            enums.Add(enumSchema);
    }

    public ConstSchema ParseConstFile(string filePath)
    {
        var constSchema = new ConstSchema();

        using var stream = OpenFileWithRetry(filePath);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var result = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false
            }
        });

        if (result.Tables.Count == 0)
            return constSchema;

        var table = result.Tables[0]; // Use first sheet

        // Format: | Id | Name | ValueType | Value | Description |
        // Skip header (row 0), type (row 1), description (row 2)
        for (int i = 3; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];

            var idStr = row[0]?.ToString()?.Trim();
            var name = row[1]?.ToString()?.Trim();
            var valueType = row[2]?.ToString()?.Trim();
            var value = row[3]?.ToString()?.Trim();
            var description = table.Columns.Count > 4 ? row[4]?.ToString()?.Trim() : string.Empty;

            // Skip empty rows
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!int.TryParse(idStr, out int id))
                throw new FormatException($"Invalid constant ID '{idStr}' for {name}");

            constSchema.Values.Add(new ConstValue
            {
                Id = id,
                Name = name,
                ValueType = valueType ?? "int",
                Value = value ?? string.Empty,
                Description = description ?? string.Empty
            });
        }

        return constSchema;
    }
}
