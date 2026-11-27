using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A plan around one great room: the <see cref="Center"/> room (a cathedral nave, a cloister garth) stands in the middle
/// of the floor ringed by a one-tile walkway, the entrance and goal open onto the walkway at opposite ends of its long
/// axis, side halls leave from the middle of each side (the transepts first), and rooms line the walkway and side halls.
/// </summary>
[Tool]
[GlobalClass]
public partial class CourtyardRoomLayout : HallRoomLayout
{
	/// <summary>The rectangular room at the heart of the floor; its doorways open onto the walkway around it.</summary>
	[Export] public RoomDefinition Center { get; set; }

	/// <summary>The goal opens onto the walkway's far end, a few tiles from the floor's edge: no room for a gauntlet.</summary>
	public override bool HoldsGauntlets => false;

	protected override List<BranchPoint> LayCore(MapData map, FloorPlan plan, FloorBuildContext context, Dictionary<Vector2I, int> halls)
	{
		if (Center == null || !Center.SupportsRealm(context.Pool.RealmId))
			throw new InvalidOperationException($"{ResourcePath}: assign a center room that suits realm '{context.Pool.RealmId}'.");
		// An authoring error must surface, not read as a center that did not fit this attempt.
		if (Center.Validate() is { Length: > 0 } errors) throw new InvalidOperationException(string.Join("\n", errors));
		var rng = context.Rng;
		bool horizontal = map.Width != map.Height ? map.Width > map.Height : rng.Randf() < .5f;
		RoomCluster center;
		try
		{
			center = context.Pool.CreateFrom(Center, rng, CreateRoomOptions(context, isProgressionRoom: true));
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		// The center room's long side runs along the floor's, either way round.
		bool centerHorizontal = center.Map.Width > center.Map.Height;
		center.Rotate((center.Map.Width != center.Map.Height && centerHorizontal != horizontal ? 1 : 0) + 2 * rng.RandiRange(0, 1));
		var size = new Vector2I(center.Map.Width, center.Map.Height);
		var position = new Vector2I((map.Width - size.X) / 2, (map.Height - size.Y) / 2);
		// The walkway rings the room a tile out, and rooms need room beyond it.
		if (position.X < 3 || position.Y < 3 || position.X + size.X > map.Width - 3 || position.Y + size.Y > map.Height - 3)
		{
			center.Free();
			return null;
		}
		AddPlacement(map, plan, center, position);

		var min = position - Vector2I.One;
		var max = position + size;
		int Along(Vector2I tile) => (horizontal ? tile.X - min.X : tile.Y - min.Y) + 1;
		for (int x = min.X; x <= max.X; x++)
		for (int y = min.Y; y <= max.Y; y++)
			if (x == min.X || x == max.X || y == min.Y || y == max.Y) halls[new Vector2I(x, y)] = Along(new Vector2I(x, y));

		var progressionOptions = CreateRoomOptions(context, isProgressionRoom: true);
		var entrance = context.Pool.CreateEntrance(rng, progressionOptions);
		if (!Attach(map, plan, entrance, halls, (order, _) => -order, rng, out int start))
		{
			entrance.Free();
			return null;
		}
		var goal = CreateGoal(context, progressionOptions);
		if (!Attach(map, plan, goal, halls, (order, _) => order, rng, out int end) || end <= start)
		{
			goal.Free();
			return null;
		}

		// Transepts leave the middle of the long sides; the short sides hold the entrance and goal, so halls there rarely fit.
		var mid = (min + max) / 2;
		var sides = horizontal
			? new[] { (new Vector2I(mid.X, min.Y), Vector2I.Up), (new Vector2I(mid.X, max.Y), Vector2I.Down), (new Vector2I(min.X, mid.Y), Vector2I.Left), (new Vector2I(max.X, mid.Y), Vector2I.Right) }
			: new[] { (new Vector2I(min.X, mid.Y), Vector2I.Left), (new Vector2I(max.X, mid.Y), Vector2I.Right), (new Vector2I(mid.X, min.Y), Vector2I.Up), (new Vector2I(mid.X, max.Y), Vector2I.Down) };
		if (rng.Randf() < .5f) (sides[0], sides[1]) = (sides[1], sides[0]);
		var points = new List<BranchPoint>(sides.Length);
		foreach (var (from, direction) in sides) points.Add(new BranchPoint(from, direction, halls[from]));
		return points;
	}
}