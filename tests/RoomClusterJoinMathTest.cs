// Godot value types are managed; pure-C# join math runs without booting the engine.
#pragma warning disable GdUnit0501
namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
public class RoomClusterJoinMathTest
{
	private static MapData RectRoomWithConnectors(int w, int h, params (Vector2I Tile, Vector2I Dir)[] connectors)
	{
		var map = new MapData(w, h);
		for (int x = 0; x < w; x++)
		for (int y = 0; y < h; y++)
			map.SetTile(x, y, MapTile.Room);
		foreach (var (tile, dir) in connectors)
			map.SetConnector(tile.X, tile.Y, [dir]);
		return map;
	}

	[TestCase]
	public void TryJoinClusterMapsBridgesWithSealedWallsAndRespectsAnchorAndIsolation()
	{
		var parent = RectRoomWithConnectors(3, 3, (new(2, 1), Vector2I.Right), (new(0, 1), Vector2I.Left));
		var child = RectRoomWithConnectors(3, 4, (new(1, 0), Vector2I.Up));

		var sysRng = new Random(7);
		bool joined = RoomAssembler.TryJoinClusterMaps(parent, child, null, n => sysRng.Next(n), out var result);
		AssertBool(joined).IsTrue();

		// 1-tile bridge exists as Corridor and both perpendicular edges are sealed.
		AssertBool(result.CombinedMap.IsCorridor(result.BridgeTile.X, result.BridgeTile.Y)).IsTrue();
		AssertBool(result.CombinedMap.IsCorridorEdgeSealed(result.BridgeTile.X, result.BridgeTile.Y, result.BridgePerpendicular)).IsTrue();
		AssertBool(result.CombinedMap.IsCorridorEdgeSealed(result.BridgeTile.X, result.BridgeTile.Y, -result.BridgePerpendicular)).IsTrue();

		// Linked connectors are internal to the cluster bridge; only parent's unlinked horizontal connector remains external.
		var external = RoomCluster.GetExternalConnectors(result.CombinedMap);
		AssertInt(external.Count).IsEqual(1);
		AssertBool(Math.Abs(external[0].Direction.X) == 1 && external[0].Tile + external[0].Direction != result.BridgeTile).IsTrue();

		// Restricting anchor tiles to West forces the join onto (0, 1), leaving only East external.
		var westOnly = new HashSet<Vector2I> { new(0, 1) };
		var narrowChild = RectRoomWithConnectors(1, 3, (new(0, 2), Vector2I.Down));
		AssertBool(RoomAssembler.TryJoinClusterMaps(parent, narrowChild, westOnly, n => sysRng.Next(n), out var westJoin)).IsTrue();
		var westExternal = RoomCluster.GetExternalConnectors(westJoin.CombinedMap);
		AssertInt(westExternal.Count).IsEqual(1);
		AssertBool(westExternal[0].Direction == Vector2I.Right).IsTrue();

		// If anchorTiles contains no connectors at all, join must return false.
		var noConnectorTiles = new HashSet<Vector2I> { new(1, 1) };
		AssertBool(RoomAssembler.TryJoinClusterMaps(parent, child, noConnectorTiles, n => sysRng.Next(n), out _)).IsFalse();
	}

	[TestCase]
	public void TryJoinClusterMapsRejectsAlignmentWhereUnlinkedConnectorFacesOtherRoomBuffer()
	{
		// Parent has East connector (2, 1) and North-East corner connector (2, 0) also pointing Right!
		// Any 3-wide child joined to (2, 1) will occupy tiles adjacent to (2, 0)+Right=(3, 0), blocking that unlinked doorway.
		var parent = RectRoomWithConnectors(3, 3, (new(2, 1), Vector2I.Right), (new(2, 0), Vector2I.Right));
		var tallWallChild = RectRoomWithConnectors(3, 3, (new(0, 1), Vector2I.Left));

		var sysRng = new Random(1);
		bool joined = RoomAssembler.TryJoinClusterMaps(parent, tallWallChild, null, n => sysRng.Next(n), out _);
		AssertBool(joined).IsFalse();
	}
}
