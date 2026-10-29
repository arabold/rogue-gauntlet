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
            ConnectorTile = roomTile;
            CorridorTile = roomTile + direction;
        }

        /// <summary>The room-edge connector tile.</summary>
        public Vector2I ConnectorTile { get; }

        /// <summary>The tile just outside the connector where a corridor enters.</summary>
        public Vector2I CorridorTile { get; }
    }

    private sealed class RoomComponent
    {
        public List<Vector2I> Tiles { get; } = new();
        public List<RoomConnector> Connectors { get; } = new();
    }

    /// <summary>
    /// A corridor to try between two entrance tiles. <see cref="Order"/> is the candidate's place in the
    /// enumeration, which breaks ties between equally short paths.
    /// </summary>
    private readonly record struct CorridorCandidate(Vector2I From, Vector2I To, int Order, RoomComponent Target)
    {
        /// <summary>The fewest tiles any path between the two can have: paths never move diagonally and count both ends.</summary>
        public int MinPathCount { get; } = Math.Abs(To.X - From.X) + Math.Abs(To.Y - From.Y) + 1;
    }

    public override void ConnectRooms(MapData map)
    {
        var astar = CreatePathGrid(map);
        var components = FindRoomComponents(map);

        // Phase 1: link every room into a single reachable network (spanning tree),
        // guaranteeing each room at least one connection.
        ConnectComponents(map, astar, components);

        // Phase 2: route a corridor out of every explicit doorway that the spanning
        // tree left unconnected. Inferred open edges stay optional.
        ConnectRemainingDoorways(map, astar, components);
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
    /// Connects every explicit doorway into the corridor network. The spanning tree
    /// links only one connector per room; doorways come from DoorwayMarkers and are
    /// intentional entrances, so any left unconnected are routed here to the nearest
    /// connector already on the network (e.g. a 4-way crossing keeps all its doors).
    /// Inferred open edges are left optional.
    /// </summary>
    private void ConnectRemainingDoorways(MapData map, AStarGrid2D astar, List<RoomComponent> components)
    {
        var connectors = new List<RoomConnector>();
        foreach (var component in components)
        {
            connectors.AddRange(component.Connectors);
        }

        foreach (var connector in connectors)
        {
            if (!map.IsDoorway(connector.ConnectorTile.X, connector.ConnectorTile.Y))
            {
                continue; // Only doorways must be connected; open edges are optional.
            }

            if (map.IsCorridor(connector.CorridorTile.X, connector.CorridorTile.Y))
            {
                continue; // Already opens into the network.
            }

            var candidates = new List<CorridorCandidate>();
            foreach (var target in connectors)
            {
                if (target.CorridorTile == connector.CorridorTile
                    || !map.IsCorridor(target.CorridorTile.X, target.CorridorTile.Y))
                {
                    continue;
                }

                candidates.Add(new CorridorCandidate(connector.CorridorTile, target.CorridorTile, candidates.Count, null));
            }

            if (TryFindShortestPath(astar, candidates, out _, out var bestPath))
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
        var candidates = new List<CorridorCandidate>();
        foreach (var connectedComponent in connectedComponents)
        {
            foreach (var startConnector in connectedComponent.Connectors)
            {
                foreach (var remainingComponent in remainingComponents)
                {
                    foreach (var endConnector in remainingComponent.Connectors)
                    {
                        candidates.Add(new CorridorCandidate(
                            startConnector.CorridorTile, endConnector.CorridorTile, candidates.Count, remainingComponent));
                    }
                }
            }
        }

        bool found = TryFindShortestPath(astar, candidates, out var best, out bestPath);
        componentToConnect = best.Target;
        return found;
    }

    /// <summary>
    /// Picks the candidate whose A* path has the fewest tiles, the earliest one on ties: the same choice as querying
    /// every candidate in order. Candidates are tried from the shortest possible path up and the search stops once
    /// none left can beat the best path found, so most A* queries never run.
    /// </summary>
    private static bool TryFindShortestPath(
        AStarGrid2D astar,
        List<CorridorCandidate> candidates,
        out CorridorCandidate best,
        out Godot.Collections.Array<Vector2I> bestPath)
    {
        candidates.Sort(static (a, b) => a.MinPathCount != b.MinPathCount
            ? a.MinPathCount.CompareTo(b.MinPathCount)
            : a.Order.CompareTo(b.Order));

        best = default;
        bestPath = null;
        int bestCount = int.MaxValue;
        foreach (var candidate in candidates)
        {
            // Every later candidate is at least as long, or as long and later, so none can win.
            if (candidate.MinPathCount > bestCount
                || (candidate.MinPathCount == bestCount && candidate.Order > best.Order))
            {
                break;
            }

            var path = astar.GetIdPath(candidate.From, candidate.To);
            if (path.Count > 0 && (path.Count < bestCount || (path.Count == bestCount && candidate.Order < best.Order)))
            {
                best = candidate;
                bestPath = path;
                bestCount = path.Count;
            }
        }

        return bestPath != null;
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
