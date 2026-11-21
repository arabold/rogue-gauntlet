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
    private sealed class RoomComponent
    {
        public List<Vector2I> Connectors { get; } = new();
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

    public override void ConnectRooms(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, RandomNumberGenerator rng) =>
        Connect(map, plan, rooms);

    internal static void Connect(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms)
    {
        using var astar = CreatePathGrid(map, plan);
        if (plan != null && plan.ApprovedEdges.Count > 0)
        {
            ConnectApprovedEdges(map, plan, rooms, astar);
            return;
        }

        var components = BuildComponents(map, plan, rooms);

        // Phase 1: link every room into a single reachable network (spanning tree),
        // guaranteeing each room at least one connection.
        ConnectComponents(map, astar, components);

        // Phase 2: route a corridor out of every doorway that the spanning tree left
        // unconnected.
        ConnectRemainingDoorways(map, astar, components);
    }

    /// <summary>
    /// Seals edges between a newly carved corridor <paramref name="tile"/> owned by <paramref name="ownerId"/>
    /// and any neighboring corridor tile belonging to another edge or protected bridge so separate branches
    /// cannot merge into bypasses.
    /// </summary>
    internal static void SealAdjacentForeignCorridors(MapData map, int[,] edgeOwner, Vector2I tile, int ownerId)
    {
        foreach (var dir in MapData.CardinalOffsets)
        {
            int nx = tile.X + dir.X;
            int nz = tile.Y + dir.Y;
            if (!map.IsWithinBounds(nx, nz) || !map.IsCorridor(nx, nz)) continue;
            if (edgeOwner[nx, nz] != ownerId)
            {
                map.SealCorridorEdge(tile, dir);
            }
        }
    }

    private static void ConnectApprovedEdges(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms, AStarGrid2D astar)
    {
        var edgeOwner = new int[map.Width, map.Height];
        for (int x = 0; x < map.Width; x++)
        {
            for (int z = 0; z < map.Height; z++)
            {
                edgeOwner[x, z] = -1;
            }
        }
        foreach (var p in plan.ProtectedCorridors)
        {
            if (map.IsWithinBounds(p.X, p.Y)) edgeOwner[p.X, p.Y] = -2;
        }

        var allExitTiles = new HashSet<Vector2I>();
        foreach (var room in rooms)
        {
            CollectExternalExits(map, plan, room, [], allExitTiles);
        }

        // Keep every doorway exit tile solid except the pair under evaluation so routed corridors
        // never trespass across unrelated doorways or fuse branches.
        foreach (var exitTile in allExitTiles)
        {
            astar.SetPointSolid(exitTile, true);
        }

        // Each edge connects exactly the exits its layout paired. Any other pairing could end one edge's corridor on
        // a doorway planned for the next edge and link two rooms around the required room between them.
        for (int edgeIndex = 0; edgeIndex < plan.ApprovedEdges.Count; edgeIndex++)
        {
            var edge = plan.ApprovedEdges[edgeIndex];
            var candidates = new List<CorridorCandidate> { new(edge.FromExit, edge.ToExit, 0, null) };
            if (!TryFindShortestPath(astar, candidates, out _, out var bestPath, (grid, candidate) =>
            {
                if (!allExitTiles.Contains(candidate.From) || !allExitTiles.Contains(candidate.To)
                    || edgeOwner[candidate.From.X, candidate.From.Y] >= 0 || edgeOwner[candidate.To.X, candidate.To.Y] >= 0)
                {
                    return null;
                }

                grid.SetPointSolid(candidate.From, false);
                grid.SetPointSolid(candidate.To, false);
                var path = candidate.From == candidate.To
                    ? [candidate.From]
                    : grid.GetIdPath(candidate.From, candidate.To);
                grid.SetPointSolid(candidate.From, true);
                grid.SetPointSolid(candidate.To, true);
                return path;
            }))
            {
                GD.Print($"Approved edge {edgeIndex} ({edge.FromClusterId} -> {edge.ToClusterId}) could not be routed; retrying layout.");
                return;
            }

            foreach (var tile in bestPath)
            {
                if (map.IsEmpty(tile.X, tile.Y))
                {
                    map.SetTile(tile.X, tile.Y, MapTile.Corridor);
                }
                edgeOwner[tile.X, tile.Y] = edgeIndex;
                astar.SetPointSolid(tile, true);
                SealAdjacentForeignCorridors(map, edgeOwner, tile, edgeIndex);
            }
        }
    }

    private static void CollectExternalExits(
        MapData map,
        FloorPlan plan,
        RoomRegion room,
        List<Vector2I> destination,
        HashSet<Vector2I> seenSet = null)
    {
        foreach (var connector in room.ConnectorTiles)
        foreach (var direction in map.GetConnectorDirections(connector.X, connector.Y))
        {
            var outside = connector + direction;
            if (plan != null && plan.ProtectedCorridors.Contains(outside)) continue;
            if (map.IsWithinBounds(outside.X, outside.Y)
                && (map.IsEmpty(outside.X, outside.Y) || map.IsCorridor(outside.X, outside.Y)))
            {
                destination.Add(outside);
                seenSet?.Add(outside);
            }
        }
    }

    private static void ConnectComponents(MapData map, AStarGrid2D astar, List<RoomComponent> components)
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
                throw new InvalidOperationException("A room has no routable doorway and cannot be reached.");
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
    /// Connects every doorway into the corridor network. The spanning tree links only
    /// one connector per room; every connector is a doorway its room built, so any left
    /// unconnected are routed here to the nearest connector already on the network
    /// (e.g. a 4-way crossing keeps all its doors).
    /// </summary>
    private static void ConnectRemainingDoorways(MapData map, AStarGrid2D astar, List<RoomComponent> components)
    {
        var connectors = new List<Vector2I>();
        foreach (var component in components)
        {
            connectors.AddRange(component.Connectors);
        }

        foreach (var connector in connectors)
        {
            if (map.IsCorridor(connector.X, connector.Y))
            {
                continue; // Already opens into the network.
            }

            var candidates = new List<CorridorCandidate>();
            foreach (var target in connectors)
            {
                if (target == connector
                    || !map.IsCorridor(target.X, target.Y))
                {
                    continue;
                }

                candidates.Add(new CorridorCandidate(connector, target, candidates.Count, null));
            }

            if (TryFindShortestPath(astar, candidates, out _, out var bestPath))
            {
                PlaceCorridorPath(map, astar, bestPath);
            }
            else
            {
                GD.PrintErr($"Doorway entrance at {connector} could not be connected.");
            }
        }
    }

    private static AStarGrid2D CreatePathGrid(MapData map, FloorPlan plan)
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
                bool protectedBridge = plan != null && plan.ProtectedCorridors.Contains(node);
                if (map.IsCorridor(x, z) && !protectedBridge)
                {
                    astar.SetPointWeightScale(node, 0);
                }

                // Corridors are routed outside rooms and protected cluster bridges. Room connectors
                // become solid here so paths cannot enter through a closed side of a doorway tile.
                if (map.IsRoom(x, z) || map.IsConnector(x, z) || map.IsWall(x, z) || map.IsChasm(x, z) || protectedBridge)
                {
                    astar.SetPointSolid(node, solid: true);
                }
            }
        }

        return astar;
    }

    /// <summary>Uses the rooms and doorway tiles already registered by the layout, grouping cluster siblings into one component.</summary>
    private static List<RoomComponent> BuildComponents(MapData map, FloorPlan plan, IReadOnlyList<RoomRegion> rooms)
    {
        var components = new List<RoomComponent>(rooms.Count);
        var byCluster = new Dictionary<int, RoomComponent>();
        foreach (var room in rooms)
        {
            RoomComponent component;
            if (room.ClusterId >= 0)
            {
                if (!byCluster.TryGetValue(room.ClusterId, out component))
                {
                    component = new RoomComponent();
                    byCluster[room.ClusterId] = component;
                    components.Add(component);
                }
            }
            else
            {
                component = new RoomComponent();
                components.Add(component);
            }

            CollectExternalExits(map, plan, room, component.Connectors);
        }
        return components;
    }

    private static bool TryFindBestConnection(
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
                            startConnector, endConnector, candidates.Count, remainingComponent));
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
        out Godot.Collections.Array<Vector2I> bestPath,
        Func<AStarGrid2D, CorridorCandidate, Godot.Collections.Array<Vector2I>> queryPath = null)
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

            var path = queryPath != null ? queryPath(astar, candidate) : astar.GetIdPath(candidate.From, candidate.To);
            if (path != null && path.Count > 0 && (path.Count < bestCount || (path.Count == bestCount && candidate.Order < best.Order)))
            {
                best = candidate;
                bestPath = path;
                bestCount = path.Count;
            }
        }

        return bestPath != null;
    }

    private static void PlaceCorridorPath(MapData map, AStarGrid2D astar, Godot.Collections.Array<Vector2I> path)
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

}
