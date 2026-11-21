using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A house plan: one hallway runs the length of the floor with the entrance at one end and the goal at the other, side
/// halls branch off it toward the map edge, and rooms line both sides of every hall. With cell-sized
/// <see cref="HallRoomLayout.SideRooms"/> it becomes a prison block.
/// </summary>
[Tool]
[GlobalClass]
public partial class HallwayRoomLayout : HallRoomLayout
{
	protected override List<BranchPoint> LayCore(MapData map, FloorPlan plan, FloorBuildContext context, Dictionary<Vector2I, int> halls)
	{
		var rng = context.Rng;
		bool horizontal = map.Width != map.Height ? map.Width > map.Height : rng.Randf() < .5f;
		int length = horizontal ? map.Width : map.Height;
		int across = horizontal ? map.Height : map.Width;
		int line = across / 2 + rng.RandiRange(-across / 8, across / 8);
		for (int along = 1; along < length - 1; along++) halls[At(horizontal, along, line)] = along;

		var progressionOptions = CreateRoomOptions(context, isProgressionRoom: true);
		var entrance = context.Pool.CreateEntrance(rng, progressionOptions);
		if (!Attach(map, plan, entrance, halls, (order, _) => -order, rng, out int start))
		{
			entrance.Free();
			return null;
		}
		var goal = CreateGoal(context, progressionOptions);
		if (!Attach(map, plan, goal, halls, (order, _) => order, rng, out int end) || end <= start + MinimumHallLength)
		{
			goal.Free();
			return null;
		}

		// The main hallway runs from the entrance's doorway to the goal's, so the walk between them passes every room.
		foreach (var tile in halls.Where(pair => pair.Value < start || pair.Value > end).Select(pair => pair.Key).ToList())
			halls.Remove(tile);

		// Side halls leave from evenly spaced points, alternating sides.
		var points = new List<BranchPoint>();
		int side = rng.Randf() < .5f ? 1 : -1;
		for (int index = 0; index < Branches; index++, side = -side)
		{
			int along = start + (end - start) * (index + 1) / (Branches + 1);
			var from = At(horizontal, along, line);
			points.Add(new BranchPoint(from, At(horizontal, along, line + side) - from, halls[from]));
		}
		return points;
	}

	private static Vector2I At(bool horizontal, int along, int across) => horizontal ? new(along, across) : new(across, along);
}