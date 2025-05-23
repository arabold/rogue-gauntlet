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
    public int Width { get; }
    public int Height { get; }
    public MapTile[,] Tiles { get; }

    public MapData(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new MapTile[width, height];
    }

    /// <summary>
    /// Checks if rooms on the given map intersect with rooms on the current map.
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
                var adjacentTiles = new[]
                {
                    new Vector2I(mapX - 1, mapZ - 1),
                    new Vector2I(mapX, mapZ - 1),
                    new Vector2I(mapX + 1, mapZ - 1),
                    new Vector2I(mapX - 1, mapZ),
                    new Vector2I(mapX, mapZ),
                    new Vector2I(mapX + 1, mapZ),
                    new Vector2I(mapX - 1, mapZ + 1),
                    new Vector2I(mapX, mapZ + 1),
                    new Vector2I(mapX + 1, mapZ + 1),
                };
                if (adjacentTiles.Any(tile => IsWithinBounds(tile.X, tile.Y) && !IsEmpty(tile.X, tile.Y)))
                {
                    GD.Print($"Room overlaps with existing room at ({mapX}, 0, {mapZ})");
                    return true;
                }
            }
        }
        return false;
    }

    public void SetTile(int x, int y, MapTile tile)
    {
        Tiles[x, y] = tile;
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

    // Helper checks: IsWall, IsRoom, etc.
    public bool IsWall(int x, int y) => Tiles[x, y] == MapTile.Wall;
    public bool IsRoom(int x, int y) => Tiles[x, y] == MapTile.Room;
    public bool IsConnector(int x, int y) => Tiles[x, y] == MapTile.Connector;
    public bool IsChasm(int x, int y) => Tiles[x, y] == MapTile.Chasm;
    public bool IsCorridor(int x, int y) => Tiles[x, y] == MapTile.Corridor;
    public bool IsEmpty(int x, int y) => Tiles[x, y] == MapTile.Empty;
    public bool IsWallOrEmpty(int x, int y) => IsEmpty(x, y) || IsWall(x, y);
}
