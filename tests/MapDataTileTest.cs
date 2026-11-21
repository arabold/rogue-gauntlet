// Godot vectors are managed value types; these tests never construct engine objects.
#pragma warning disable GdUnit0501
namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Pure tile math: the map is centered on the world origin and tiles are Room.TileSize wide.</summary>
[TestSuite]
public class MapDataTileTest
{
	[TestCase]
	public void TileCentersRoundTripAndEveryPointInsideATileMapsBackToIt()
	{
		var map = new MapData(21, 20);
		AssertBool(map.TileToWorld(10, 10) == Vector3I.Zero).IsTrue();
		for (int x = 0; x < map.Width; x++)
		{
			for (int z = 0; z < map.Height; z++)
			{
				var tile = new Vector2I(x, z);
				Vector3 center = map.TileToWorld(tile);
				AssertBool(map.WorldToTile(center) == tile).IsTrue();
				float inside = Room.TileSize * .49f;
				AssertBool(map.WorldToTile(center + new Vector3(inside, 3, -inside)) == tile).IsTrue();
			}
		}
		AssertBool(map.WorldToTile(new Vector3(Room.TileSize * .51f, 0, 0)) == new Vector2I(11, 10)).IsTrue();
	}

	[TestCase]
	public void SealedEdgesTurnStampClearAndOpenNeighborCountPreserveWallTruth()
	{
		var map = new MapData(5, 5);
		map.ResetToBorderedEmpty();
		map.SetTile(1, 1, MapTile.Corridor);
		map.SetTile(2, 1, MapTile.Corridor);
		map.SetTile(1, 2, MapTile.Corridor);

		map.SetConnector(1, 2, [Vector2I.Up]);
		map.SealCorridorEdge(new Vector2I(1, 1), Vector2I.Right);
		AssertBool(map.IsCorridorEdgeSealed(1, 1, Vector2I.Right)).IsTrue();
		AssertBool(map.IsCorridorEdgeSealed(2, 1, Vector2I.Left)).IsTrue();
		AssertBool(map.HasWallToward(1, 1, Vector2I.Right)).IsTrue();
		AssertBool(map.HasWallToward(2, 1, Vector2I.Left)).IsTrue();
		AssertInt(map.OpenNeighborCount(1, 1)).IsEqual(1);
		AssertInt(map.OpenNeighborCount(2, 1)).IsEqual(0);
		AssertInt(map.OpenNeighborCount(1, 2)).IsEqual(1);

		// Out-of-bounds sealing on border does not throw or leak into interior.
		map.SealCorridorEdge(new Vector2I(0, 0), Vector2I.Left);

		// Rotating 4 quarter turns rotates every sealed edge and connector bitmask, returning cleanly at turn 4.
		var turned = map;
		Vector2I tile = new(1, 1);
		Vector2I dir = Vector2I.Right;
		Vector2I conn = new(1, 2);
		Vector2I connDir = Vector2I.Up;
		for (int turn = 1; turn <= 4; turn++)
		{
			turned = turned.Turned();
			tile = MapData.TurnTile(tile, 5);
			dir = MapData.TurnDirection(dir);
			conn = MapData.TurnTile(conn, 5);
			connDir = MapData.TurnDirection(connDir);
			AssertBool(turned.IsCorridorEdgeSealed(tile.X, tile.Y, dir)).IsTrue();
			AssertBool(turned.IsConnectorOpenToward(conn.X, conn.Y, connDir)).IsTrue();
		}
		AssertBool(tile == new Vector2I(1, 1) && dir == Vector2I.Right).IsTrue();

		// Restore non-connector tile (1, 2) back to Corridor for remaining checks.
		map.SetTile(1, 2, MapTile.Corridor);

		// ClearCorridorEdge opens both sides symmetrically.
		map.ClearCorridorEdge(new Vector2I(1, 1), Vector2I.Right);
		AssertBool(map.IsCorridorEdgeSealed(1, 1, Vector2I.Right)).IsFalse();
		AssertBool(map.IsCorridorEdgeSealed(2, 1, Vector2I.Left)).IsFalse();
		AssertInt(map.OpenNeighborCount(1, 1)).IsEqual(2);
		AssertInt(map.OpenNeighborCount(2, 1)).IsEqual(1);

		// SetTile to Empty clears sealed bits on both sides.
		map.SealCorridorEdge(new Vector2I(1, 1), Vector2I.Right);
		map.SetTile(2, 1, MapTile.Empty);
		AssertBool(map.IsCorridorEdgeSealed(1, 1, Vector2I.Right)).IsFalse();
		AssertBool(map.IsCorridorEdgeSealed(2, 1, Vector2I.Left)).IsFalse();

		// Stamp preserves non-empty tiles, connector directions, and sealed edges.
		var stampSource = new MapData(2, 2);
		stampSource.SetTile(0, 0, MapTile.Corridor);
		stampSource.SetTile(1, 0, MapTile.Corridor);
		stampSource.SealCorridorEdge(new Vector2I(0, 0), Vector2I.Right);
		stampSource.SetConnector(0, 1, [Vector2I.Left]);
		var dest = new MapData(6, 6);
		dest.Stamp(stampSource, new Vector2I(2, 2));
		AssertBool(dest.IsCorridor(2, 2) && dest.IsCorridor(3, 2)).IsTrue();
		AssertBool(dest.IsCorridorEdgeSealed(2, 2, Vector2I.Right)).IsTrue();
		AssertBool(dest.IsCorridorEdgeSealed(3, 2, Vector2I.Left)).IsTrue();
		AssertBool(dest.IsConnectorOpenToward(2, 3, Vector2I.Left)).IsTrue();
		AssertBool(dest.IsConnectorOpenToward(2, 3, Vector2I.Right)).IsFalse();

		// Intersects with buffer=0 forbids out-of-bounds placement and allows flush contact.
		var piece = new MapData(1, 1);
		piece.SetTile(0, 0, MapTile.Room);
		AssertBool(dest.Intersects(piece, new Vector2I(-1, 0), buffer: 0)).IsTrue();
		AssertBool(dest.Intersects(piece, new Vector2I(1, 2), buffer: 0)).IsFalse();
		AssertBool(dest.Intersects(piece, new Vector2I(1, 2), buffer: 1)).IsTrue();
	}

	[TestCase]
	public void WorldToTileSpaceAndHasWorldSightMatchOddWidthCenters()
	{
		var map = new MapData(21, 20);
		for (int x = 10; x <= 14; x++)
		{
			map.SetTile(x, 10, MapTile.Corridor);
		}

		// World origin sits at the center of tile (10, 10) on an odd-width (21) map.
		var tileSpace = map.WorldToTileSpace(Vector3.Zero);
		AssertBool(Mathf.IsEqualApprox(tileSpace.X, 10.5f) && Mathf.IsEqualApprox(tileSpace.Y, 10.5f)).IsTrue();
		AssertBool(map.HasWorldSight(Vector3.Zero, map.TileToWorld(14, 10))).IsTrue();

		map.SealCorridorEdge(new Vector2I(12, 10), Vector2I.Right);
		AssertBool(map.HasWorldSight(Vector3.Zero, map.TileToWorld(14, 10))).IsFalse();
	}

	[TestCase]
	public void TurnedRotatesMultiDirectionConnectorBitmasksWithoutLegacyLists()
	{
		var map = new MapData(5, 5);
		var conn = new Vector2I(1, 0);
		map.SetConnector(conn.X, conn.Y, [Vector2I.Right, Vector2I.Down]);

		var turned = map;
		Vector2I d0 = Vector2I.Right;
		Vector2I d1 = Vector2I.Down;
		for (int step = 1; step <= 4; step++)
		{
			turned = turned.Turned();
			conn = MapData.TurnTile(conn, 5);
			d0 = MapData.TurnDirection(d0);
			d1 = MapData.TurnDirection(d1);
			AssertInt(turned.GetConnectorDirections(conn.X, conn.Y).Count).IsEqual(2);
			AssertBool(turned.IsConnectorOpenToward(conn.X, conn.Y, d0)).IsTrue();
			AssertBool(turned.IsConnectorOpenToward(conn.X, conn.Y, d1)).IsTrue();
		}
	}
}