using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Optional, room-specific clutter and light policy, applied after the required composition.</summary>
[Tool, GlobalClass]
public partial class RoomDressingProfile : Resource
{
	[Export] public RoomArrangementPool Clutter { get; set; }
	[Export] public RoomLightingProfile Lighting { get; set; }

	public string[] Validate(float passageRadius = 1)
	{
		var errors = (Clutter?.Validate() ?? []).Concat(Lighting?.Validate() ?? []).ToList();
		if (Clutter != null && Clutter.PassageRadius < passageRadius)
			errors.Add("Dressing: clutter passage radius must cover the required room clearance.");
		return errors.ToArray();
	}

	/// <summary>Each optional pass sees accepted furniture from earlier passes and cannot compromise passage.</summary>
	public List<RoomArrangementPlacement> Plan(MapData map, RoomRegion room, ulong seed, int attempt,
		Func<Vector2I, Vector3> tileCenter, IReadOnlyList<Aabb> obstacles, IReadOnlyList<Aabb> walls, IReadOnlyList<Aabb> passageObstacles = null, float passageRadius = 1)
	{
		var errors = Validate(passageRadius);
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
		var result = new List<RoomArrangementPlacement>();
		var occupied = obstacles.ToList();
		var blocking = (passageObstacles ?? obstacles).ToList();
		if (Lighting != null)
		{
			var lights = Lighting.Plan(map, room, seed, attempt, tileCenter, occupied, walls, blocking, passageRadius);
			result.AddRange(lights.Select(p => p with { IsDetail = true }));
			occupied.AddRange(GetBounds(lights));
			blocking.AddRange(GetBounds(lights, true));
		}
		if (Clutter != null)
		{
			var clutter = Clutter.PlacementStrategy.Plan(map, [room], Clutter,
				GenerationRandom.DeriveSeed(seed, GenerationRandom.Decoration, "room-clutter"), attempt, tileCenter, occupied, walls, blocking);
			result.AddRange(clutter.Select(p => p with { IsDetail = true }));
		}
		return result;
	}

	/// <summary>Capture value bounds and free temporary scene nodes before returning to the caller.</summary>
	public static IReadOnlyList<Aabb> GetBounds(IEnumerable<RoomArrangementPlacement> placements, bool blockingOnly = false)
	{
		var result = new List<Aabb>();
		foreach (var placement in placements)
		{
			var instance = placement.Definition.Instantiate();
			try { result.AddRange((blockingOnly ? instance.GetBlockingBounds() : instance.GetLocalBounds()).Select(box => placement.Transform * box)); }
			finally { instance.Free(); }
		}
		return result;
	}
}