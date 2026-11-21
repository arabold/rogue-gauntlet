namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Strict layouts route each approved corridor between the doorway exits the layout paired.</summary>
[TestSuite, RequireGodotRuntime]
public class ApprovedEdgeRoutingTest
{
	/// <summary>
	/// A → B → C, where B's lower doorway and C's upper doorway share the exit tile T between them. T is closer to A than
	/// B's planned top doorway, but a corridor from A ending on T would reach C without passing through B.
	/// </summary>
	[TestCase]
	public void CorridorsJoinOnlyThePairedExitsSoNoRoomIsBypassed()
	{
		var map = new MapData(16, 12);
		map.ResetToBorderedEmpty();
		var a = Box(map, 0, 1, 6, 3, 3, ((3, 7), Vector2I.Right));
		var b = Box(map, 1, 6, 2, 4, 3, ((7, 2), Vector2I.Up), ((8, 4), Vector2I.Down));
		var c = Box(map, 2, 7, 6, 3, 3, ((8, 6), Vector2I.Up));
		var shared = new Vector2I(8, 5);
		var plan = new FloorPlan();
		plan.ApprovedEdges.Add(new ApprovedEdge(0, 1, new Vector2I(4, 7), new Vector2I(7, 1)));
		plan.ApprovedEdges.Add(new ApprovedEdge(1, 2, shared, shared));

		AStarCorridorConnector.Connect(map, plan, [a, b, c]);

		foreach (var connector in new[] { a, b, c }.SelectMany(room => room.ConnectorTiles))
		foreach (var direction in map.GetConnectorDirections(connector.X, connector.Y))
			AssertBool(map.IsCorridor(connector.X + direction.X, connector.Y + direction.Y)).IsTrue();
		var aroundB = AccessSolutionValidator.Reachable(map, a.Tiles[0], b.Tiles.Concat(b.ConnectorTiles).ToHashSet());
		AssertBool(c.Tiles.Any(aroundB.Contains)).IsFalse();
	}

	/// <summary>A rectangular room of its own cluster with the given doorways.</summary>
	private static RoomRegion Box(MapData map, int id, int x0, int z0, int width, int height,
		params ((int X, int Z) Tile, Vector2I Direction)[] doorways)
	{
		var region = new RoomRegion(id) { ClusterId = id };
		for (int x = x0; x < x0 + width; x++)
		for (int z = z0; z < z0 + height; z++)
		{
			map.SetTile(x, z, MapTile.Room);
			region.Tiles.Add(new Vector2I(x, z));
		}
		foreach (var ((x, z), direction) in doorways)
		{
			map.SetConnector(x, z, [direction]);
			region.ConnectorTiles.Add(new Vector2I(x, z));
		}
		return region;
	}
}