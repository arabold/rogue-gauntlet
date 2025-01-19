using System.Collections.Generic;
using System.Linq;
using Godot;

public enum MapTile
{
    Empty,
    Wall,
    Room,
    Corridor,
}

/// <summary>
/// Represents the level map
/// </summary>
public class MapData
{
    public int Width { get; }
    public int Height { get; }
    public MapTile[,] Tiles { get; }
    private bool[,] Occupied;

    public MapData(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new MapTile[width, height];
        Occupied = new bool[width, height];
    }

    /// <summary>
    /// Checks if rooms on the given map intersect with rooms on the current map
    /// </summary>
    public bool Intersects(MapData roomMap, Vector3I placement)
    {
        for (var x = 0; x < roomMap.Width; x++)
        {
            for (var z = 0; z < roomMap.Height; z++)
            {
                if (roomMap.IsEmpty(x, z))
                    continue; // Empty cells are fine

                var mapX = placement.X + x;
                var mapZ = placement.Z + z;
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

    public bool IsOccupied(int x, int y)
    {
        if (!IsWithinBounds(x, y))
            return true; // Treat out-of-bounds as occupied

        return Occupied[x, y];
    }

    public void MarkOccupied(int x, int y)
    {
        if (IsWithinBounds(x, y))
        {
            Occupied[x, y] = true;
        }
    }

    public bool IsWithinBounds(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    // Helper checks: IsWall, IsRoom, etc.
    public bool IsWall(int x, int y) => Tiles[x, y] == MapTile.Wall;
    public bool IsRoom(int x, int y) => Tiles[x, y] == MapTile.Room;
    public bool IsCorridor(int x, int y) => Tiles[x, y] == MapTile.Corridor;
    public bool IsEmpty(int x, int y) => Tiles[x, y] == MapTile.Empty;
    public bool IsWallOrEmpty(int x, int y) => IsEmpty(x, y) || IsWall(x, y);
}
