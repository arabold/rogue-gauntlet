namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Actor-sized walkability: what an actor of the navigation radius can stand on and walk between.</summary>
[TestSuite, RequireGodotRuntime]
public class WalkabilityGridTest
{
	private static WalkabilityGrid OpenFloor(float size = 12)
	{
		var grid = new WalkabilityGrid(new Rect2(0, 0, size, size), 1);
		grid.AddFloor(new Rect2(0, 0, size, size));
		return grid;
	}

	/// <summary>A wall across the floor at x = 6 with a centered gap.</summary>
	private static WalkabilityGrid Divided(float gap)
	{
		var grid = OpenFloor();
		grid.AddObstacle(new Rect2(5.75f, 0, .5f, 6 - gap / 2));
		grid.AddObstacle(new Rect2(5.75f, 6 + gap / 2, .5f, 6 - gap / 2));
		return grid;
	}

	[TestCase]
	public void GateWideOpeningsPassAndNarrowGapsBlock()
	{
		AssertBool(Divided(2.4f).AreConnected([new(2, 6), new(10, 6)])).IsTrue();
		AssertBool(Divided(1.6f).AreConnected([new(2, 6), new(10, 6)])).IsFalse();
	}

	[TestCase]
	public void SealedEnclosuresLeaveStandablePocketsOutOfReach()
	{
		var grid = OpenFloor(16);
		grid.AddObstacle(new Rect2(6, 6, 6, .5f));
		grid.AddObstacle(new Rect2(6, 11.5f, 6, .5f));
		grid.AddObstacle(new Rect2(6, 6, .5f, 6));
		grid.AddObstacle(new Rect2(11.5f, 6, .5f, 6));
		var reached = grid.Reachable([new Vector2(2, 2)]);
		var inside = grid.CellAt(new Vector2(9, 9));
		AssertBool(grid.IsStandable(inside)).IsTrue();
		AssertBool(reached[inside.Y * grid.Size.X + inside.X]).IsFalse();
	}

	[TestCase]
	public void ConvexOutlinesBlockOnlyTheirTrueShape()
	{
		// The diagonal hull of a corner wall: its bounding square would block (3.5, 3.5) too.
		var grid = OpenFloor();
		grid.AddObstacle([new Vector2(0, 0), new Vector2(4, 0), new Vector2(0, 4)]);
		AssertBool(grid.IsStandable(new Vector2(.5f, .5f))).IsFalse();
		AssertBool(grid.IsStandable(new Vector2(3.5f, 3.5f))).IsTrue();
	}

	[TestCase]
	public void OnlySolidsInTheActorHeightBandBlock()
	{
		var grid = OpenFloor();
		grid.AddObstacle(new Aabb(new Vector3(5, 2.2f, 5), new Vector3(1, .5f, 1)));
		grid.AddObstacle(new Aabb(new Vector3(5, 0, 7), new Vector3(1, .1f, 1)));
		AssertBool(grid.IsStandable(new Vector2(5.5f, 5.5f))).IsTrue();
		AssertBool(grid.IsStandable(new Vector2(5.5f, 7.5f))).IsTrue();
		grid.AddObstacle(new Aabb(new Vector3(5, 0, 5), new Vector3(1, 1, 1)));
		AssertBool(grid.IsStandable(new Vector2(5.5f, 5.5f))).IsFalse();
	}

	[TestCase]
	public void RestoringASnapshotUndoesLaterObstacles()
	{
		var grid = OpenFloor();
		var saved = grid.Save();
		grid.AddObstacle(new Rect2(5, 5, 2, 2));
		AssertBool(grid.IsStandable(new Vector2(6, 6))).IsFalse();
		grid.Restore(saved);
		AssertBool(grid.IsStandable(new Vector2(6, 6))).IsTrue();
	}
}