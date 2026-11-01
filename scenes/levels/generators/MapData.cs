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

    /// <summary>The four edge neighbors of a tile.</summary>
    public static readonly IReadOnlyList<Vector2I> CardinalOffsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public int Width { get; }
    public int Height { get; }
    public MapTile[,] Tiles { get; }
    private readonly Dictionary<Vector2I, List<Vector2I>> _connectorDirections = new();

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
            _connectorDirections.Remove(new Vector2I(x, y));
        }
    }

    /// <summary>
    /// Marks a tile as a room connector, a doorway every corridor must connect, and stores
    /// the directions it opens toward.
    /// </summary>
    public void SetConnector(int x, int y, IEnumerable<Vector2I> directions)
    {
        Tiles[x, y] = MapTile.Connector;

        var key = new Vector2I(x, y);
        var openDirections = _connectorDirections.TryGetValue(key, out var existingDirections)
            ? existingDirections.Concat(directions).Distinct().ToList()
            : directions.Distinct().ToList();
        if (openDirections.Count == 0)
        {
            _connectorDirections.Remove(key);
            return;
        }

        _connectorDirections[key] = openDirections;
    }

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
    /// World position of a tile's center. The generator sits at the world origin with the
    /// map centered on it, so this is also the GridMap cell the tile's content is anchored at.
    /// </summary>
    public Vector3I TileToWorld(int x, int z) => new((x - Width / 2) * Room.TileSize, 0, (z - Height / 2) * Room.TileSize);

    public Vector3I TileToWorld(Vector2I tile) => TileToWorld(tile.X, tile.Y);

    /// <summary>The tile containing a world position; the inverse of <see cref="TileToWorld(int,int)"/>.</summary>
    public Vector2I WorldToTile(Vector3 world) => new(
        Mathf.RoundToInt(world.X / Room.TileSize) + Width / 2,
        Mathf.RoundToInt(world.Z / Room.TileSize) + Height / 2);

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
    }

    /// <summary>
    /// This map a quarter turn on, the way <see cref="Room.Rotate"/> turns its room: tiles
    /// move by <see cref="TurnTile"/> and connector directions by <see cref="TurnDirection"/>.
    /// </summary>
    public MapData Turned()
    {
        var turned = new MapData(Height, Width);
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                var to = TurnTile(new Vector2I(x, y), Width);
                if (IsConnector(x, y))
                {
                    turned.SetConnector(to.X, to.Y, GetConnectorDirections(x, y).Select(TurnDirection));
                }
                else
                {
                    turned.SetTile(to.X, to.Y, Tiles[x, y]);
                }
            }
        }

        return turned;
    }

    /// <summary>
    /// Where tile (x, y) of a <paramref name="width"/>-wide map lands after a quarter turn:
    /// local +X turns toward -Z, so the turned map's first column is the old first row.
    /// </summary>
    public static Vector2I TurnTile(Vector2I tile, int width) => new(tile.Y, width - 1 - tile.X);

    /// <summary>A cardinal direction after the quarter turn of <see cref="TurnTile"/>.</summary>
    public static Vector2I TurnDirection(Vector2I direction) => new(direction.Y, -direction.X);

    /// <summary>
    /// The Y rotation, in degrees, that turns content authored facing north (-Z), such as a
    /// door, toward the cardinal <paramref name="direction"/>.
    /// </summary>
    public static float FacingYaw(Vector2I direction) => (direction.X, direction.Y) switch
    {
        (1, 0) => 270f,
        (0, 1) => 180f,
        (-1, 0) => 90f,
        _ => 0f,
    };

    // Helper checks: IsWall, IsRoom, etc.
    public bool IsWall(int x, int y) => Tiles[x, y] == MapTile.Wall;
    public bool IsRoom(int x, int y) => Tiles[x, y] == MapTile.Room;
    public bool IsConnector(int x, int y) => Tiles[x, y] == MapTile.Connector;
    public bool IsChasm(int x, int y) => Tiles[x, y] == MapTile.Chasm;
    public bool IsCorridor(int x, int y) => Tiles[x, y] == MapTile.Corridor;
    public bool IsEmpty(int x, int y) => Tiles[x, y] == MapTile.Empty;
    public bool IsWallOrEmpty(int x, int y) => IsEmpty(x, y) || IsWall(x, y);
    public bool IsWalkable(int x, int y) => IsRoom(x, y) || IsConnector(x, y) || IsCorridor(x, y);

    /// <summary>Playable surfaces exploration can uncover, chasms included; everything else is void that stays capped.</summary>
    public bool IsExplorable(int x, int y) => IsWithinBounds(x, y) && (IsWalkable(x, y) || IsChasm(x, y));
}
