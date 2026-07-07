using System.Collections.Generic;
using System.Linq;
using Godot;

public enum MapTile
{
    Empty,
    Wall,
    Room,
    Connector,
    Corridor,
    Chasm,
}

/// <summary>
/// Represents the level map
/// </summary>
public class MapData
{
    private static readonly IReadOnlyList<Vector2I> EmptyConnectorDirections = System.Array.Empty<Vector2I>();

    public int Width { get; }
    public int Height { get; }
    public MapTile[,] Tiles { get; }
    private readonly Dictionary<Vector2I, List<Vector2I>> _connectorDirections = new();
    // Connectors that come from an explicit DoorwayMarker (vs. inferred open edges).
    // These are intentional entrances and must all be connected by a corridor.
    private readonly HashSet<Vector2I> _doorwayTiles = new();

    public MapData(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new MapTile[width, height];
    }

    /// <summary>
    /// Checks if rooms on the given map intersect with rooms on the current map (with a
    /// 1-tile buffer on every side, so placed rooms never end up directly adjacent).
    /// </summary>
    public bool Intersects(MapData roomMap, Vector2I placement)
    {
        for (var x = 0; x < roomMap.Width; x++)
        {
            for (var y = 0; y < roomMap.Height; y++)
            {
                if (roomMap.IsEmpty(x, y))
                    continue; // Empty cells are fine

                var mapX = placement.X + x;
                var mapZ = placement.Y + y;
                // Check all nine tiles around the room
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (IsWithinBounds(mapX + dx, mapZ + dz) && !IsEmpty(mapX + dx, mapZ + dz))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    public void SetTile(int x, int y, MapTile tile)
    {
        Tiles[x, y] = tile;

        if (tile != MapTile.Connector)
        {
            var key = new Vector2I(x, y);
            _connectorDirections.Remove(key);
            _doorwayTiles.Remove(key);
        }
    }

    /// <summary>
    /// Marks a tile as a room connector and stores the directions where the room has
    /// no wall. <paramref name="isDoorway"/> flags connectors that come from an
    /// explicit DoorwayMarker (intentional entrances that must all be connected).
    /// </summary>
    public void SetConnector(int x, int y, IEnumerable<Vector2I> directions, bool isDoorway = false)
    {
        Tiles[x, y] = MapTile.Connector;

        var key = new Vector2I(x, y);
        var openDirections = _connectorDirections.TryGetValue(key, out var existingDirections)
            ? existingDirections.Concat(directions).Distinct().ToList()
            : directions.Distinct().ToList();
        if (openDirections.Count == 0)
        {
            _connectorDirections.Remove(key);
            _doorwayTiles.Remove(key);
            return;
        }

        _connectorDirections[key] = openDirections;
        if (isDoorway)
        {
            _doorwayTiles.Add(key);
        }
    }

    /// <summary>
    /// True when the connector tile is an explicit doorway (from a DoorwayMarker),
    /// as opposed to an inferred open edge.
    /// </summary>
    public bool IsDoorway(int x, int y) => _doorwayTiles.Contains(new Vector2I(x, y));

    /// <summary>
    /// Returns the open sides for a connector tile.
    /// </summary>
    public IReadOnlyList<Vector2I> GetConnectorDirections(int x, int y)
    {
        return _connectorDirections.TryGetValue(new Vector2I(x, y), out var directions)
            ? directions
            : EmptyConnectorDirections;
    }

    /// <summary>
    /// Checks if the given cell is within the bounds of the map.
    /// </summary>
    public bool IsWithinBounds(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    /// <summary>
    /// Checks if the given cell is on the boundary of the map.
    /// </summary>
    public bool IsOnBoundary(int x, int y)
    {
        return x == 0 || y == 0 || x == Width - 1 || y == Height - 1;
    }

    /// <summary>
    /// True when a wall must separate the walkable tile at (x, y) from its walkable
    /// neighbor in <paramref name="direction"/>. Rooms open onto corridors only through
    /// connector tiles, and a connector only through its open directions — any other
    /// room/corridor contact must be sealed. This matters for tightly packed layouts,
    /// where corridors are routed directly alongside room edges: without this rule an
    /// unwalled room edge next to a passing corridor would become an unintended hole.
    /// Void-facing edges are handled separately (see MapGenerator.PlaceWalls).
    /// </summary>
    public bool RequiresInteriorWall(int x, int y, Vector2I direction)
    {
        int nx = x + direction.X;
        int ny = y + direction.Y;
        if (!IsWithinBounds(nx, ny))
        {
            return false;
        }

        // Room interior meeting a corridor: sealed unless a connector sanctions it.
        if (IsRoom(x, y) && IsCorridor(nx, ny))
        {
            return true;
        }
        if (IsCorridor(x, y) && IsRoom(nx, ny))
        {
            return true;
        }

        // A connector is a passage only along its open directions.
        if (IsConnector(x, y) && IsCorridor(nx, ny))
        {
            return !GetConnectorDirections(x, y).Contains(direction);
        }
        if (IsCorridor(x, y) && IsConnector(nx, ny))
        {
            return !GetConnectorDirections(nx, ny).Contains(-direction);
        }

        return false;
    }

    /// <summary>
    /// Resets every tile to Empty and walls the outer border, and clears connector
    /// bookkeeping — the initial state for a fresh map, and for re-stamping rooms after
    /// a placement is rolled back.
    /// </summary>
    public void ResetToBorderedEmpty()
    {
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                Tiles[x, y] = IsOnBoundary(x, y) ? MapTile.Wall : MapTile.Empty;
            }
        }

        _connectorDirections.Clear();
        _doorwayTiles.Clear();
    }

    // Helper checks: IsWall, IsRoom, etc.
    public bool IsWall(int x, int y) => Tiles[x, y] == MapTile.Wall;
    public bool IsRoom(int x, int y) => Tiles[x, y] == MapTile.Room;
    public bool IsConnector(int x, int y) => Tiles[x, y] == MapTile.Connector;
    public bool IsChasm(int x, int y) => Tiles[x, y] == MapTile.Chasm;
    public bool IsCorridor(int x, int y) => Tiles[x, y] == MapTile.Corridor;
    public bool IsEmpty(int x, int y) => Tiles[x, y] == MapTile.Empty;
    public bool IsWallOrEmpty(int x, int y) => IsEmpty(x, y) || IsWall(x, y);
    public bool IsWalkable(int x, int y) => IsRoom(x, y) || IsConnector(x, y) || IsCorridor(x, y);
}
