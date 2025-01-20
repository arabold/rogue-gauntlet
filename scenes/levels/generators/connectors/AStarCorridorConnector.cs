using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Connects the rooms in the base map with corridors using A* pathfinding.
/// </summary>
[Tool]
[GlobalClass]
public partial class AStarCorridorConnector : CorridorConnectorStrategy
{
    public override void ConnectRooms(MapData map)
    {
        // We use a simple A* pathfinding algorithm to connect the rooms.
        // Initialize AStarGrid2D
        AStarGrid2D astar = new AStarGrid2D();
        astar.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never;
        astar.Region = new Rect2I(0, 0, map.Width, map.Height);
        astar.CellSize = Vector2.One;
        astar.Update();

        // Initialize the weights
        astar.FillSolidRegion(new Rect2I(0, 0, map.Width, map.Height), solid: true);
        astar.FillSolidRegion(new Rect2I(1, 1, map.Width - 1, map.Height - 1), solid: false);
        astar.FillWeightScaleRegion(new Rect2I(0, 0, map.Width, map.Height), 5);

        // Find all corridor tiles on the map
        List<Vector2I> corridorTiles = new List<Vector2I>();
        for (int x = 0; x < map.Width; x++)
        {
            for (int z = 0; z < map.Height; z++)
            {
                var node = new Vector2I(x, z);

                if (map.IsCorridor(x, z))
                {
                    corridorTiles.Add(node);
                    astar.SetPointWeightScale(node, 0);
                }

                if (map.IsRoom(x, z) || map.IsWall(x, z))
                {
                    // Avoid walking through rooms
                    astar.SetPointSolid(node, solid: true);
                }
            }
        }

        GD.Print($"Found {corridorTiles.Count} corridor tiles to connect.");

        // Pick two random floor tiles to connect
        var maxTries = 100;
        while (corridorTiles.Count > 1 && maxTries-- > 0)
        {
            ShuffleList(corridorTiles);
            Vector2I tile1 = corridorTiles[0];
            Vector2I tile2 = corridorTiles[1];
            if (ConnectTiles(map, astar, tile1, tile2))
            {
                GD.Print($"Connected tiles {tile1} and {tile2}");
                // remove the connected tiles
                corridorTiles.Remove(tile1);
            }
        }

        if (maxTries <= 0)
        {
            GD.PrintErr("Failed to connect all rooms.");
        }
    }

    private bool ConnectTiles(MapData map, AStarGrid2D astar, Vector2I tile1, Vector2I tile2)
    {
        var path = astar.GetIdPath(tile1, tile2);
        if (path.Count > 0)
        {
            foreach (var node in path)
            {
                if (map.IsEmpty(node.X, node.Y))
                {
                    map.SetTile(node.X, node.Y, MapTile.Corridor);
                    astar.SetPointWeightScale(node, 0);
                }
            }
            return true;
        }
        return false;
    }

    // Shuffle a list in place using Fisher-Yates algorithm
    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = GD.RandRange(0, i);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
