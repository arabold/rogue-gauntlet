// Godot vectors are managed value types; these tests never construct engine objects.
#pragma warning disable GdUnit0501
namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Analytic sight cases independent of the renderer, generation and physics engine.</summary>
[TestSuite]
public class FogOfWarRevealTest
{
	[TestCase]
	public void OpenSpaceRevealsACircleRatherThanAWholeConnectedRoom()
	{
		var discovery = new DungeonDiscovery(20, 20);
		discovery.Reveal(new Vector2(10, 10), 2, (_, _) => true);
		AssertBool(discovery.Explored.Count > 600).IsTrue();
		AssertBool(discovery.Explored.All(i =>
			new Vector2((i % discovery.Width + 0.5f) / 8, (i / discovery.Width + 0.5f) / 8).DistanceTo(new Vector2(10, 10)) <= 2)).IsTrue();
		AssertBool(discovery.Explored.Any(i => discovery.TileAt(i) == new Vector2I(11, 10))).IsTrue();
		AssertBool(discovery.Explored.Any(i => discovery.TileAt(i) == new Vector2I(13, 10))).IsFalse();
	}

	[TestCase]
	public void WallBlocksSightAndAnOpeningRevealsOnlyItsVisibleWedge()
	{
		// A vertical wall at x=3, with a one-tile opening at y=2.
		bool Across(Vector2I a, Vector2I b) => a.X == b.X || Math.Min(a.X, b.X) != 2 || a.Y == 2;
		AssertBool(DungeonDiscovery.HasSight(new Vector2(1.5f, 2.5f), new Vector2(5.5f, 2.5f), Across)).IsTrue();
		AssertBool(DungeonDiscovery.HasSight(new Vector2(1.5f, 2.5f), new Vector2(3.5f, 4.5f), Across)).IsFalse();
		AssertBool(DungeonDiscovery.HasSight(new Vector2(1.5f, 1.5f), new Vector2(4.5f, 1.5f), Across)).IsFalse();
	}

	[TestCase]
	public void DiagonalCornersDoNotLeakAndDiscoveryNeverRefogs()
	{
		bool Across(Vector2I a, Vector2I b) => a != new Vector2I(1, 0) && b != new Vector2I(1, 0);
		AssertBool(DungeonDiscovery.HasSight(new Vector2(0.5f, 0.5f), new Vector2(2.5f, 2.5f), Across)).IsFalse();
		var discovery = new DungeonDiscovery(8, 8);
		discovery.Reveal(new Vector2(2.5f, 2.5f), 2, (_, _) => true);
		int[] remembered = discovery.Explored.ToArray();
		discovery.Reveal(new Vector2(6.5f, 6.5f), 1, (_, _) => false);
		AssertBool(remembered.All(discovery.Explored.Contains)).IsTrue();
	}

	[TestCase]
	public void ClosingADoorStopsNewDiscoveryWithoutErasingHistory()
	{
		var discovery = new DungeonDiscovery(12, 5);
		bool open = false;
		bool Across(Vector2I a, Vector2I b) => open || a.X == b.X || Math.Min(a.X, b.X) != 4;
		discovery.Reveal(new Vector2(4.5f, 2.5f), 2, Across);
		AssertBool(discovery.Explored.Any(i => discovery.TileAt(i).X > 4)).IsFalse();
		open = true;
		discovery.Reveal(new Vector2(4.5f, 2.5f), 2, Across);
		int count = discovery.Explored.Count;
		AssertBool(discovery.Explored.Any(i => discovery.TileAt(i).X > 4)).IsTrue();
		open = false;
		discovery.Reveal(new Vector2(4.5f, 2.5f), 4, Across);
		AssertBool(discovery.Explored.Count >= count).IsTrue();
		AssertBool(discovery.Explored.Any(i => discovery.TileAt(i).X > 6)).IsFalse();
	}

	[TestCase]
	public void RaysAlongAxesAndFromTileEdgesTerminate()
	{
		foreach (var target in new[] { new Vector2(0.5f, 3.5f), new Vector2(3.5f, 0.5f), new Vector2(0.5f, 0.5f) })
			AssertBool(DungeonDiscovery.HasSight(new Vector2(3, 3), target, (_, _) => true)).IsTrue();
	}
}