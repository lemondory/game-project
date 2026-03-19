namespace GameServer.Game;

/// <summary>
/// Spatial hash grid for Area of Interest (AOI) queries.
/// Not thread-safe — must only be accessed from the game loop thread.
/// CellSize=30f; ViewRadius=50f covers a 3×3 cell neighbourhood at most.
/// </summary>
public class AoiGrid
{
    private const float CellSize = 30f;

    private readonly Dictionary<(int cx, int cz), HashSet<long>> _cells   = new();
    private readonly Dictionary<long, (int cx, int cz)>          _entityCell = new();

    private static (int cx, int cz) GetCell(float x, float z)
        => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize));

    // ── Write API ─────────────────────────────────────────────────────────────

    public void Add(long entityId, float x, float z)
    {
        var cell = GetCell(x, z);
        _entityCell[entityId] = cell;

        if (!_cells.TryGetValue(cell, out var set))
        {
            set = new HashSet<long>();
            _cells[cell] = set;
        }
        set.Add(entityId);
    }

    /// <summary>No-op if the entity hasn't changed cells.</summary>
    public void Update(long entityId, float x, float z)
    {
        var newCell = GetCell(x, z);

        if (_entityCell.TryGetValue(entityId, out var oldCell))
        {
            if (oldCell == newCell)
                return;

            if (_cells.TryGetValue(oldCell, out var oldSet))
            {
                oldSet.Remove(entityId);
                if (oldSet.Count == 0)
                    _cells.Remove(oldCell);
            }
        }

        _entityCell[entityId] = newCell;

        if (!_cells.TryGetValue(newCell, out var newSet))
        {
            newSet = new HashSet<long>();
            _cells[newCell] = newSet;
        }
        newSet.Add(entityId);
    }

    public void Remove(long entityId)
    {
        if (!_entityCell.Remove(entityId, out var cell))
            return;

        if (_cells.TryGetValue(cell, out var set))
        {
            set.Remove(entityId);
            if (set.Count == 0)
                _cells.Remove(cell);
        }
    }

    // ── Read API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Yields entity IDs in the cells that overlap the circle (AABB approximation).
    /// Caller must apply a precise squared-distance check against <paramref name="radius"/>.
    /// </summary>
    public IEnumerable<long> GetEntityIdsInRange(float x, float z, float radius)
    {
        int cellRadius = (int)MathF.Ceiling(radius / CellSize);
        var (cx, cz)   = GetCell(x, z);

        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        for (int dz = -cellRadius; dz <= cellRadius; dz++)
        {
            if (_cells.TryGetValue((cx + dx, cz + dz), out var set))
                foreach (var id in set)
                    yield return id;
        }
    }
}
