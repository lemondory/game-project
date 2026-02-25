using System;
using System.IO;
using MessagePack;

namespace GameShared.Data;

/// <summary>
/// Base class for data management
/// GameDataManager inherits from this class
/// </summary>
public abstract class DataManagerBase
{
    protected string DataPath { get; private set; } = string.Empty;
    public bool IsLoaded { get; private set; }

    protected DataManagerBase()
    {
        // Protected constructor for inheritance
    }

    /// <summary>
    /// Initialize and load all data tables
    /// </summary>
    /// <param name="dataPath">Directory containing .bytes files</param>
    public void Initialize(string dataPath)
    {
        if (IsLoaded)
        {
            Console.WriteLine("[GameDataManager] Already loaded. Skipping.");
            return;
        }

        DataPath = dataPath;

        if (!Directory.Exists(dataPath))
        {
            throw new DirectoryNotFoundException($"Data directory not found: {dataPath}");
        }

        Console.WriteLine($"[GameDataManager] Loading data from: {dataPath}");

        try
        {
            LoadAllTables();
            IsLoaded = true;
            Console.WriteLine("[GameDataManager] All data loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameDataManager] Failed to load data: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Reload all data tables
    /// </summary>
    public void Reload()
    {
        if (!IsLoaded)
        {
            Initialize(DataPath);
            return;
        }

        Console.WriteLine("[GameDataManager] Reloading data...");
        LoadAllTables();
        Console.WriteLine("[GameDataManager] Reload complete");
    }

    /// <summary>
    /// Clear all loaded data
    /// </summary>
    public void Clear()
    {
        ClearAllTables();
        IsLoaded = false;
        DataPath = string.Empty;
        Console.WriteLine("[GameDataManager] Data cleared");
    }

    /// <summary>
    /// Load a specific table from file
    /// </summary>
    protected T LoadTable<T>(string fileName) where T : class
    {
        var filePath = Path.Combine(DataPath, fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Data file not found: {filePath}");
        }

        var bytes = File.ReadAllBytes(filePath);
        var table = MessagePackSerializer.Deserialize<T>(bytes, MessagePackSerializerOptions.Standard);

        // Auto-build indexes if BuildIndexes method exists
        var buildIndexesMethod = table.GetType().GetMethod("BuildIndexes");
        if (buildIndexesMethod != null)
        {
            buildIndexesMethod.Invoke(table, null);
        }

        Console.WriteLine($"  ✓ Loaded {fileName} ({bytes.Length} bytes, {GetTableCount(table)} records)");

        return table;
    }

    private int GetTableCount(object table)
    {
        // Try to get Count property via reflection
        var countProperty = table.GetType().GetProperty("Count");
        if (countProperty != null)
        {
            return (int)(countProperty.GetValue(table) ?? 0);
        }
        return 0;
    }

    /// <summary>
    /// Override this to load all specific tables
    /// </summary>
    protected virtual void LoadAllTables()
    {
        // Override in partial class to load specific tables
        // Example:
        // Monsters = LoadTable<MonsterDataTable>("MonsterData.bytes");
        // Items = LoadTable<ItemDataTable>("ItemData.bytes");
    }

    /// <summary>
    /// Override this to clear all specific tables
    /// </summary>
    protected virtual void ClearAllTables()
    {
        // Override in partial class
    }
}
