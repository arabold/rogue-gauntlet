namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Monster navigation on the clearance grid: bodies fit where their radius fits, doors gate cells, routes keep to the
/// middle of passages, flee fields leave dead ends alone, and point routes straighten in open space.
/// </summary>
[TestSuite, RequireGodotRuntime]
public class ClearanceNavigationTest
{
	private static readonly NavigationCosts Centered = new(2, 1);
	private static readonly NavigationCosts Shortest = new(0, 1);

	private static ClearanceGrid OpenFloor(float width = 12, float depth = 12)
	{
		var grid = new ClearanceGrid(new Rect2(0, 0, width, depth));
		grid.AddFloor(new Rect2(0, 0, width, depth));
		return grid;
	}

	/// <summary>A half-unit wall across the floor at x = 6 with a centered gap.</summary>
	private static ClearanceGrid Divided(float gap)
	{
		var grid = OpenFloor();
		grid.AddObstacle(new Rect2(5.75f, 0, .5f, 6 - gap / 2));
		grid.AddObstacle(new Rect2(5.75f, 6 + gap / 2, .5f, 6 - gap / 2));
		return grid;
	}

	/// <summary>A closed corridor along X between walls at z 4 and 8; <paramref name="length"/> long.</summary>
	private static ClearanceGrid Corridor(float length)
	{
		var grid = OpenFloor(length + 2, 12);
		grid.AddObstacle(new Rect2(0, 0, length + 2, 4));
		grid.AddObstacle(new Rect2(0, 8, length + 2, 4));
		grid.AddObstacle(new Rect2(0, 0, 1, 12));
		grid.AddObstacle(new Rect2(length + 1, 0, 1, 12));
		return grid;
	}

	private static bool Reaches(ClearanceGrid grid, float radius, bool[] gates, NavigationCosts costs = default) =>
		FlowField.Toward(grid, new Vector2(10, 6), radius, gates, costs, float.PositiveInfinity, 1).Reaches(grid.CellAt(new Vector2(2, 6)));

	/// <summary>Cells visited walking downhill from <paramref name="start"/>, ending where the field has no lower neighbor.</summary>
	private static List<Vector2I> Descend(FlowField field, Vector2 start, int limit = 400)
	{
		var cell = field.Grid.CellAt(start);
		var walked = new List<Vector2I> { cell };
		while (walked.Count < limit && field.TryDescend(cell, out cell)) walked.Add(cell);
		return walked;
	}

	[TestCase]
	public void ClearanceIsTheDistanceToTheNearestSolid()
	{
		var grid = Divided(2.4f);
		AssertFloat(grid.ClearanceAt(new Vector2(4.75f, 1.25f))).IsEqualApprox(1f, .01f);
		AssertFloat(grid.ClearanceAt(new Vector2(5.75f, 1.25f))).IsEqualApprox(0f, .01f);
		AssertFloat(grid.ClearanceAt(new Vector2(-1, 6))).IsEqualApprox(0f, .01f);
		// A cell's clearance is its roomiest sample: the far edge of the cell around (4.75, 1.25).
		var cell = grid.CellAt(new Vector2(4.75f, 1.25f));
		AssertFloat(grid.ClearanceAt(cell)).IsEqualApprox(1.25f, .01f);
		AssertVector(grid.BestPoint(cell)).IsEqualApprox(new Vector2(4.5f, 1.25f), new Vector2(.01f, .26f));
	}

	[TestCase]
	public void GapsNarrowerThanTheBodyStayClosedWhereverTheGridFalls()
	{
		// Shift the wall by fractions of a cell: a gap narrower than the body never admits it, and one a quarter cell
		// wider than the body always does, whatever the alignment.
		foreach (float shift in new[] { 0f, .1f, .2f, .3f, .4f })
		foreach (var (gap, open) in new[] { (1.7f, false), (1.95f, false), (2.3f, true) })
		{
			var grid = OpenFloor();
			grid.AddObstacle(new Rect2(5.75f, 0, .5f, 6 + shift - gap / 2));
			grid.AddObstacle(new Rect2(5.75f, 6 + shift + gap / 2, .5f, 6 - shift - gap / 2));
			bool reaches = FlowField.Toward(grid, new Vector2(10, 6), 1, null, Centered, float.PositiveInfinity, 1)
				.Reaches(grid.CellAt(new Vector2(2, 6)));
			AssertBool(reaches).OverrideFailureMessage($"gap {gap} shifted {shift}").IsEqual(open);
		}
	}

	[TestCase]
	public void BodiesPassGapsTheyFitThrough()
	{
		var grid = Divided(2.4f);
		AssertBool(Reaches(grid, .35f, null)).IsTrue();
		AssertBool(Reaches(grid, 1f, null)).IsTrue();
		AssertBool(Reaches(grid, 1.3f, null)).IsFalse();
	}

	[TestCase]
	public void ClosedGatesSplitFieldsUntilTheyOpen()
	{
		var grid = Divided(2.4f);
		int gate = grid.AddGate([new(5.75f, 4.8f), new(6.25f, 4.8f), new(6.25f, 7.2f), new(5.75f, 7.2f)]);
		var gates = new bool[grid.GateCount];
		AssertBool(Reaches(grid, .75f, gates)).IsFalse();
		gates[gate] = true;
		AssertBool(Reaches(grid, .75f, gates)).IsTrue();
	}

	[TestCase]
	public void RoutesKeepToTheMiddleOfPassages()
	{
		var grid = Corridor(20);
		var goal = new Vector2(19, 4.6f);
		var start = new Vector2(2, 4.6f);
		var centered = Descend(FlowField.Toward(grid, goal, .5f, null, Centered, float.PositiveInfinity, 1), start);
		var shortest = Descend(FlowField.Toward(grid, goal, .5f, null, Shortest, float.PositiveInfinity, 1), start);
		float Midway(List<Vector2I> walked) => grid.BestPoint(walked.Find(cell => grid.CellCenter(cell).X > 10)).Y;
		// Clearance past the body radius plus the preferred margin costs nothing: the middle half unit of the corridor.
		AssertFloat(Mathf.Abs(Midway(centered) - 6)).IsLessEqual(.55f);
		AssertFloat(Mathf.Abs(Midway(shortest) - 6)).IsGreaterEqual(.95f);
	}

	[TestCase]
	public void FleeingFollowsTheEscapeRouteRatherThanASideAlcove()
	{
		// A dead-end alcove opens north of the corridor at x 13-15.
		var withAlcove = new ClearanceGrid(new Rect2(0, 0, 32, 16));
		withAlcove.AddFloor(new Rect2(0, 0, 32, 16));
		withAlcove.AddObstacle(new Rect2(0, 0, 32, 4));
		withAlcove.AddObstacle(new Rect2(0, 8, 12.5f, 8));
		withAlcove.AddObstacle(new Rect2(15.5f, 8, 16.5f, 8));
		withAlcove.AddObstacle(new Rect2(12.5f, 12, 3, 4));
		withAlcove.AddObstacle(new Rect2(0, 0, 1, 16));
		withAlcove.AddObstacle(new Rect2(31, 0, 1, 16));
		var chase = FlowField.Toward(withAlcove, new Vector2(9, 6), .5f, null, Centered, float.PositiveInfinity, 1);
		var walked = Descend(FlowField.Away(chase, 1.2f, Centered, null), new Vector2(12, 6));
		AssertBool(walked.TrueForAll(cell => withAlcove.CellCenter(cell).Y < 8.5f)).IsTrue();
		AssertFloat(withAlcove.CellCenter(walked[^1]).X).IsGreater(25f);
	}

	[TestCase]
	public void ACorneredFleeStopsInItsDeadEnd()
	{
		// The threat stands near the corridor's far end: there is no way past it to anywhere farther.
		var grid = Corridor(10);
		var chase = FlowField.Toward(grid, new Vector2(10, 6), .5f, null, Centered, float.PositiveInfinity, 1);
		var walked = Descend(FlowField.Away(chase, 1.2f, Centered, null), new Vector2(4, 6));
		AssertFloat(grid.CellCenter(walked[^1]).X).IsLess(2f);
	}

	[TestCase]
	public void PointRoutesGoStraightInOpenSpaceAndBendAroundWalls()
	{
		var waypoints = new List<Vector2>();
		AssertBool(GridPath.TryFind(OpenFloor(), new(1.5f, 1.5f), new(10.5f, 10.5f), .5f, null, Centered, 5000, 1, waypoints)).IsTrue();
		AssertInt(waypoints.Count).IsEqual(1);
		AssertVector(waypoints[0]).IsEqualApprox(new Vector2(10.5f, 10.5f), new Vector2(.01f, .01f));

		var divided = Divided(2.4f);
		AssertBool(GridPath.TryFind(divided, new(2, 1.5f), new(10, 1.5f), .5f, null, Centered, 5000, 1, waypoints)).IsTrue();
		AssertInt(waypoints.Count).IsGreater(1);
		var from = new Vector2(2, 1.5f);
		foreach (var point in waypoints)
		{
			AssertBool(divided.HasClearLine(from, point, .5f, null)).IsTrue();
			from = point;
		}
		AssertBool(GridPath.TryFind(Divided(.5f), new(2, 6), new(10, 6), .5f, null, Centered, 5000, 1, waypoints)).IsFalse();
		AssertInt(waypoints.Count).IsEqual(0);
	}
}