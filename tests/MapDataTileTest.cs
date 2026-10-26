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
}