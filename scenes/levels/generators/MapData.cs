public enum MapTile
{
    Empty,
    Wall,
    Room,
    Hallway,
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

        // Initialize all tiles as empty and walls for borders
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tiles[x, y] = MapTile.Empty;

                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    Tiles[x, y] = MapTile.Wall;
                }
            }
        }
    }

    public void SetTile(int x, int y, MapTile tile)
    {
        Tiles[x, y] = tile;
    }

    public bool IsWithinBounds(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    // Helper checks: IsWall, IsRoom, etc.
    public bool IsWall(int x, int y) => Tiles[x, y] == MapTile.Wall;
    public bool IsRoom(int x, int y) => Tiles[x, y] == MapTile.Room;
    public bool IsHallway(int x, int y) => Tiles[x, y] == MapTile.Hallway;
    public bool IsEmpty(int x, int y) => Tiles[x, y] == MapTile.Empty;
    public bool IsWallOrEmpty(int x, int y) => IsEmpty(x, y) || IsWall(x, y);
}
