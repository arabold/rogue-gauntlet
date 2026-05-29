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
    private readonly struct RoomConnector
    {
        public RoomConnector(Vector2I roomTile, Vector2I direction)
        {
            CorridorTile = roomTile + direction;
        }

        public Vector2I CorridorTile { get; }
    }

    private sealed class RoomComponent
    {
        public List<Vector2I> Tiles { get; } = new();
        public List<RoomConnector> Connectors { get; } = new();
    }

    public override void ConnectRooms(MapData map)
    {
        var astar = CreatePathGrid(map);
        var components = FindRoomComponents(map);

        // Phase 1: link every room into a single reachable network (spanning tree).
        ConnectComponents(map, astar, components);

        // Phase 2: route a corridor out of every doorway, so each one is an open
        // passage rather than just one per room.
        ConnectAllDoorways(map, astar, components);
    }

    private void ConnectComponents(MapData map, AStarGrid2D astar, List<RoomComponent> components)
    {
        if (components.Count <= 1)
        {
            return;
        }

        var connectedComponents = new List<RoomComponent>();
        var remainingComponents = new List<RoomComponent>();
        foreach (var component in components)
        {
            if (component.Connectors.Count == 0)
            {
                GD.PrintErr($"Room component at {component.Tiles[0]} has no connector tiles and cannot be reached.");
                continue;
            }

            if (connectedComponents.Count == 0)
            {
                connectedComponents.Add(component);
            }
            else
            {
                remainingComponents.Add(component);
            }
        }

        GD.Print($"Found {components.Count} room components to connect.");

        while (remainingComponents.Count > 0)
        {
            if (!TryFindBestConnection(astar, connectedComponents, remainingComponents,
                out var componentToConnect, out var path))
            {
                GD.PrintErr("Failed to connect all rooms.");
                return;
            }

            PlaceCorridorPath(map, astar, path);
            connectedComponents.Add(componentToConnect);
            remainingComponents.Remove(componentToConnect);
        }
    }

    /// <summary>
    /// Connects every doorway into the corridor network. The spanning tree links
    /// only one doorway per room; the rest are routed here to the nearest doorway
    /// that already opens into the network, so authored multi-door rooms (e.g. a
    /// 4-way crossing) keep all of their entrances.
    /// </summary>
    private void ConnectAllDoorways(MapData map, AStarGrid2D astar, List<RoomComponent> components)
    {
        var connectors = new List<RoomConnector>();
        foreach (var component in components)
        {
            connectors.AddRange(component.Connectors);
        }

        foreach (var connector in connectors)
        {
            if (map.IsCorridor(connector.CorridorTile.X, connector.CorridorTile.Y))
            {
                continue; // Already opens into the network.
            }

            Godot.Collections.Array<Vector2I> bestPath = null;
            int bestLength = int.MaxValue;
            foreach (var target in connectors)
            {
                if (target.CorridorTile == connector.CorridorTile
                    || !map.IsCorridor(target.CorridorTile.X, target.CorridorTile.Y))
                {
                    continue;
                }

                var path = astar.GetIdPath(connector.CorridorTile, target.CorridorTile);
                if (path.Count > 0 && path.Count < bestLength)
                {
                    bestLength = path.Count;
                    bestPath = path;
                }
            }

            if (bestPath != null)
            {
                PlaceCorridorPath(map, astar, bestPath);
            }
            else
            {
                GD.PrintErr($"Doorway entrance at {connector.CorridorTile} could not be connected.");
            }
        }
    }

    private AStarGrid2D CreatePathGrid(MapData map)
    {
        var astar = new AStarGrid2D();
        astar.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never;
        astar.Region = new Rect2I(0, 0, map.Width, map.Height);
        astar.CellSize = Vector2.One;
        astar.Update();

        astar.FillSolidRegion(new Rect2I(0, 0, map.Width, map.Height), solid: false);
        astar.FillWeightScaleRegion(new Rect2I(0, 0, map.Width, map.Height), 5);

        for (int x = 0; x < map.Width; x++)
        {
            for (int z = 0; z < map.Height; z++)
            {
                var node = new Vector2I(x, z);
                if (map.IsCorridor(x, z))
                {
                    astar.SetPointWeightScale(node, 0);
                }

                // Corridors are routed outside rooms. Room connectors become solid
                // here so paths cannot enter through a closed side of a doorway tile.
                if (map.IsRoom(x, z) || map.IsConnector(x, z) || map.IsWall(x, z) || map.IsChasm(x, z))
                {
                    astar.SetPointSolid(node, solid: true);
                }
            }
        }

        return astar;
    }

    private List<RoomComponent> FindRoomComponents(MapData map)
    {
        var components = new List<RoomComponent>();
        var visited = new bool[map.Width, map.Height];

        for (int x = 0; x < map.Width; x++)
        {
            for (int z = 0; z < map.Height; z++)
            {
                if (!visited[x, z] && IsRoomComponentTile(map, x, z))
                {
                    components.Add(FloodFillRoomComponent(map, x, z, visited));
                }
            }
        }

        return components;
    }

    private RoomComponent FloodFillRoomComponent(MapData map, int startX, int startZ, bool[,] visited)
    {
        var component = new RoomComponent();
        var queue = new Queue<Vector2I>();
        queue.Enqueue(new Vector2I(startX, startZ));
        visited[startX, startZ] = true;

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            component.Tiles.Add(tile);

            if (map.IsConnector(tile.X, tile.Y))
            {
                AddConnectorEntrances(map, component, tile);
            }

            foreach (var neighbor in GetCardinalNeighbors(tile))
            {
                if (map.IsWithinBounds(neighbor.X, neighbor.Y)
                    && !visited[neighbor.X, neighbor.Y]
                    && IsRoomComponentTile(map, neighbor.X, neighbor.Y))
                {
                    visited[neighbor.X, neighbor.Y] = true;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return component;
    }

    private void AddConnectorEntrances(MapData map, RoomComponent component, Vector2I connectorTile)
    {
        foreach (var direction in map.GetConnectorDirections(connectorTile.X, connectorTile.Y))
        {
            var corridorTile = connectorTile + direction;
            if (map.IsWithinBounds(corridorTile.X, corridorTile.Y)
                && (map.IsEmpty(corridorTile.X, corridorTile.Y) || map.IsCorridor(corridorTile.X, corridorTile.Y)))
            {
                component.Connectors.Add(new RoomConnector(connectorTile, direction));
            }
        }
    }

    private bool TryFindBestConnection(
        AStarGrid2D astar,
        List<RoomComponent> connectedComponents,
        List<RoomComponent> remainingComponents,
        out RoomComponent componentToConnect,
        out Godot.Collections.Array<Vector2I> bestPath)
    {
        componentToConnect = null;
        bestPath = null;
        var bestPathLength = int.MaxValue;

        foreach (var connectedComponent in connectedComponents)
        {
            foreach (var startConnector in connectedComponent.Connectors)
            {
                foreach (var remainingComponent in remainingComponents)
                {
                    foreach (var endConnector in remainingComponent.Connectors)
                    {
                        var path = astar.GetIdPath(startConnector.CorridorTile, endConnector.CorridorTile);
                        if (path.Count > 0 && path.Count < bestPathLength)
                        {
                            bestPathLength = path.Count;
                            bestPath = path;
                            componentToConnect = remainingComponent;
                        }
                    }
                }
            }
        }

        return componentToConnect != null;
    }

    private void PlaceCorridorPath(MapData map, AStarGrid2D astar, Godot.Collections.Array<Vector2I> path)
    {
        foreach (var node in path)
        {
            if (map.IsEmpty(node.X, node.Y))
            {
                map.SetTile(node.X, node.Y, MapTile.Corridor);
                astar.SetPointWeightScale(node, 0);
            }
        }
    }

    private bool IsRoomComponentTile(MapData map, int x, int z)
    {
        return map.IsRoom(x, z) || map.IsConnector(x, z);
    }

    private IEnumerable<Vector2I> GetCardinalNeighbors(Vector2I tile)
    {
        yield return tile + Vector2I.Right;
        yield return tile + Vector2I.Left;
        yield return tile + Vector2I.Down;
        yield return tile + Vector2I.Up;
    }
}
