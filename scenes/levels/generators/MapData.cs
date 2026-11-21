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
    /// <summary>The four edge neighbors of a tile.</summary>
    public static readonly IReadOnlyList<Vector2I> CardinalOffsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
    private static readonly Vector2I[][] DirectionsByMask = BuildDirectionsByMask();

    public int Width { get; }
    public int Height { get; }
    public MapTile[,] Tiles { get; }
    // 4-bit cardinal masks (Up=1, Right=2, Down=4, Left=8) keep hot-path wall, connector, and discovery checks allocation-free.
    private readonly byte[,] _sealedEdges;
    private readonly byte[,] _connectorMasks;

    public MapData(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new MapTile[width, height];
        _sealedEdges = new byte[width, height];
        _connectorMasks = new byte[width, height];
    }

    private static Vector2I[][] BuildDirectionsByMask()
    {
        var table = new Vector2I[16][];
        Vector2I[] dirs = [Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left];
        for (int mask = 0; mask < 16; mask++)
        {
            int count = 0;
            for (int bit = 0; bit < 4; bit++)
                if ((mask & (1 << bit)) != 0) count++;
            var list = count == 0 ? System.Array.Empty<Vector2I>() : new Vector2I[count];
            int idx = 0;
            for (int bit = 0; bit < 4; bit++)
                if ((mask & (1 << bit)) != 0) list[idx++] = dirs[bit];
            table[mask] = list;
        }
        return table;
    }

    private static byte DirectionBit(Vector2I direction) => (direction.X, direction.Y) switch
    {
        (0, -1) => 1,
        (1, 0) => 2,
        (0, 1) => 4,
        (-1, 0) => 8,
        _ => 0,
    };

    private static byte RotateMaskLeft(byte mask) => (byte)(((mask >> 1) | ((mask & 1) << 3)) & 0xF);

    /// <summary>
    /// Checks if rooms on the given map intersect with rooms on the current map (with a
    /// <paramref name="buffer"/>-tile margin on every side, so placed rooms keep their minimum spacing).
    /// </summary>
    public bool Intersects(MapData roomMap, Vector2I placement, int buffer = 1)
    {
        for (var x = 0; x < roomMap.Width; x++)
        {
            for (var y = 0; y < roomMap.Height; y++)
            {
                if (roomMap.IsEmpty(x, y))
                    continue; // Empty cells are fine

                var mapX = placement.X + x;
                var mapZ = placement.Y + y;
                if (!IsWithinBounds(mapX, mapZ))
                    return true;

                for (int dx = -buffer; dx <= buffer; dx++)
                {
                    for (int dz = -buffer; dz <= buffer; dz++)
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
        var previous = Tiles[x, y];
        Tiles[x, y] = tile;

        if (tile != MapTile.Connector)
        {
            _connectorMasks[x, y] = 0;
        }

        ClearSealedEdgesIfLeavingCorridor(x, y, previous, tile);
    }

    private void ClearSealedEdgesIfLeavingCorridor(int x, int y, MapTile previous, MapTile next)
    {
        if (previous == MapTile.Corridor && next != MapTile.Corridor)
        {
            ClearTileSealedEdges(x, y);
        }
        else if (next != MapTile.Corridor)
        {
            _sealedEdges[x, y] = 0;
        }
    }

    private void ClearTileSealedEdges(int x, int y)
    {
        _sealedEdges[x, y] = 0;
        foreach (var offset in CardinalOffsets)
        {
            int nx = x + offset.X;
            int ny = y + offset.Y;
            if (IsWithinBounds(nx, ny))
            {
                _sealedEdges[nx, ny] = (byte)(_sealedEdges[nx, ny] & ~DirectionBit(-offset));
            }
        }
    }

    /// <summary>Seals a shared edge between adjacent corridor tiles (and symmetrically on the neighbor if in bounds).</summary>
    public void SealCorridorEdge(Vector2I tile, Vector2I direction)
    {
        byte bit = DirectionBit(direction);
        if (bit == 0 || !IsWithinBounds(tile.X, tile.Y))
        {
            return;
        }

        _sealedEdges[tile.X, tile.Y] |= bit;
        int nx = tile.X + direction.X;
        int ny = tile.Y + direction.Y;
        if (IsWithinBounds(nx, ny))
        {
            _sealedEdges[nx, ny] |= DirectionBit(-direction);
        }
    }

    /// <summary>Opens a sealed corridor-to-corridor edge symmetrically.</summary>
    public void ClearCorridorEdge(Vector2I tile, Vector2I direction)
    {
        byte bit = DirectionBit(direction);
        if (bit == 0 || !IsWithinBounds(tile.X, tile.Y))
        {
            return;
        }

        _sealedEdges[tile.X, tile.Y] = (byte)(_sealedEdges[tile.X, tile.Y] & ~bit);
        int nx = tile.X + direction.X;
        int ny = tile.Y + direction.Y;
        if (IsWithinBounds(nx, ny))
        {
            _sealedEdges[nx, ny] = (byte)(_sealedEdges[nx, ny] & ~DirectionBit(-direction));
        }
    }

    public bool IsCorridorEdgeSealed(int x, int y, Vector2I direction) =>
        IsWithinBounds(x, y) && (_sealedEdges[x, y] & DirectionBit(direction)) != 0;

    /// <summary>
    /// Marks a tile as a room connector, a doorway every corridor must connect, and stores
    /// the directions it opens toward.
    /// </summary>
    public void SetConnector(int x, int y, IEnumerable<Vector2I> directions)
    {
        var previous = Tiles[x, y];
        Tiles[x, y] = MapTile.Connector;
        ClearSealedEdgesIfLeavingCorridor(x, y, previous, MapTile.Connector);

        byte mask = previous == MapTile.Connector ? _connectorMasks[x, y] : (byte)0;
        foreach (var direction in directions)
        {
            mask |= DirectionBit(direction);
        }
        _connectorMasks[x, y] = mask;
    }

    public bool IsConnectorOpenToward(int x, int y, Vector2I direction) =>
        IsWithinBounds(x, y) && (_connectorMasks[x, y] & DirectionBit(direction)) != 0;

    /// <summary>
    /// Returns the open sides for a connector tile.
    /// </summary>
    public IReadOnlyList<Vector2I> GetConnectorDirections(int x, int y) =>
        IsWithinBounds(x, y) ? DirectionsByMask[_connectorMasks[x, y]] : DirectionsByMask[0];

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

    /// <summary>The world XZ rectangle every tile covers, edge to edge: the frame floor-wide grids (exploration,
    /// walkability, the wall mask) share.</summary>
    public Rect2 WorldBounds
    {
        get
        {
            Vector3I first = TileToWorld(0, 0);
            return new Rect2(first.X - Room.TileSize / 2f, first.Z - Room.TileSize / 2f, Width * Room.TileSize, Height * Room.TileSize);
        }
    }

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
        if ((IsRoom(x, y) || IsChasm(x, y)) && IsCorridor(nx, ny))
        {
            return true;
        }
        if (IsCorridor(x, y) && (IsRoom(nx, ny) || IsChasm(nx, ny)))
        {
            return true;
        }

        // A connector is a passage only along its open directions.
        if (IsConnector(x, y) && IsCorridor(nx, ny))
        {
            return (_connectorMasks[x, y] & DirectionBit(direction)) == 0;
        }
        if (IsCorridor(x, y) && IsConnector(nx, ny))
        {
            return (_connectorMasks[nx, ny] & DirectionBit(-direction)) == 0;
        }

        // Adjacent corridor tiles may be separated by an explicit partition edge.
        if (IsCorridor(x, y) && IsCorridor(nx, ny))
        {
            return IsCorridorEdgeSealed(x, y, direction);
        }

        return false;
    }

    /// <summary>
    /// Single source of truth for whether a wall separates tile (<paramref name="x"/>, <paramref name="z"/>) from
    /// its neighbor in <paramref name="direction"/> (void/border, unsanctioned room/corridor contact, or sealed corridor edge).
    /// </summary>
    public bool HasWallToward(int x, int z, Vector2I direction)
    {
        int nx = x + direction.X;
        int nz = z + direction.Y;
        return !IsWithinBounds(nx, nz) || IsWallOrEmpty(nx, nz) || RequiresInteriorWall(x, z, direction);
    }

    /// <summary>True when stepping between adjacent tiles stays on walkable floor without crossing an interior wall.</summary>
    public bool CanSeeAcross(Vector2I from, Vector2I to) =>
        IsWithinBounds(from.X, from.Y) && IsWithinBounds(to.X, to.Y)
        && IsWalkable(from.X, from.Y) && IsWalkable(to.X, to.Y)
        && !RequiresInteriorWall(from.X, from.Y, to - from);

    /// <summary>True when an unobstructed structural sightline connects the centers of tiles <paramref name="fromTile"/> and <paramref name="toTile"/>.</summary>
    public bool HasTileSight(Vector2I fromTile, Vector2I toTile) =>
        fromTile == toTile || DungeonDiscovery.HasSight(
            new Vector2(fromTile.X + 0.5f, fromTile.Y + 0.5f),
            new Vector2(toTile.X + 0.5f, toTile.Y + 0.5f),
            CanSeeAcross);

    /// <summary>Continuous tile-space coordinates corresponding to a world XZ position (matching <see cref="TileToWorld"/>).</summary>
    public Vector2 WorldToTileSpace(Vector3 world) =>
        new(world.X / Room.TileSize + Width / 2 + 0.5f, world.Z / Room.TileSize + Height / 2 + 0.5f);

    /// <summary>True when an unobstructed structural sightline connects two world XZ positions.</summary>
    public bool HasWorldSight(Vector3 fromWorld, Vector3 toWorld) =>
        DungeonDiscovery.HasSight(WorldToTileSpace(fromWorld), WorldToTileSpace(toWorld), CanSeeAcross);

    /// <summary>Counts cardinal walkable neighbors reachable without crossing a wall.</summary>
    public int OpenNeighborCount(int x, int z)
    {
        int count = 0;
        foreach (var offset in CardinalOffsets)
        {
            int nx = x + offset.X;
            int nz = z + offset.Y;
            if (IsWithinBounds(nx, nz) && IsWalkable(nx, nz) && !RequiresInteriorWall(x, z, offset))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Stamps all non-empty tiles, connectors, and sealed edges from <paramref name="source"/> at <paramref name="offset"/>.
    /// </summary>
    public void Stamp(MapData source, Vector2I offset)
    {
        for (int x = 0; x < source.Width; x++)
        {
            for (int y = 0; y < source.Height; y++)
            {
                int mapX = offset.X + x;
                int mapZ = offset.Y + y;
                if (source.IsConnector(x, y))
                {
                    SetConnector(mapX, mapZ, source.GetConnectorDirections(x, y));
                }
                else if (!source.IsEmpty(x, y))
                {
                    SetTile(mapX, mapZ, source.Tiles[x, y]);
                }
            }
        }

        for (int x = 0; x < source.Width; x++)
        {
            for (int y = 0; y < source.Height; y++)
            {
                byte mask = source._sealedEdges[x, y];
                if (mask == 0)
                {
                    continue;
                }

                var targetTile = new Vector2I(offset.X + x, offset.Y + y);
                foreach (var direction in CardinalOffsets)
                {
                    if ((mask & DirectionBit(direction)) != 0)
                    {
                        SealCorridorEdge(targetTile, direction);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resets every tile to Empty and walls the outer border, and clears connector and edge
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

        System.Array.Clear(_connectorMasks);
        System.Array.Clear(_sealedEdges);
    }

    /// <summary>
    /// This map a quarter turn on, the way <see cref="Room.Rotate"/> turns its room: tiles
    /// move by <see cref="TurnTile"/> and connector directions and sealed edges by <see cref="TurnDirection"/>.
    /// </summary>
    public MapData Turned()
    {
        var turned = new MapData(Height, Width);
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                var to = TurnTile(new Vector2I(x, y), Width);
                turned.Tiles[to.X, to.Y] = Tiles[x, y];

                byte connMask = _connectorMasks[x, y];
                if (connMask != 0)
                {
                    turned._connectorMasks[to.X, to.Y] = RotateMaskLeft(connMask);
                }

                byte mask = _sealedEdges[x, y];
                if (mask != 0)
                {
                    turned._sealedEdges[to.X, to.Y] = RotateMaskLeft(mask);
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
