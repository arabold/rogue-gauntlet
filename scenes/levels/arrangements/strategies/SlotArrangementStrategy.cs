using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Fill the generator's exact slots using the common physical checks; incomplete room compositions fail explicitly.</summary>
[Tool, GlobalClass]
public partial class SlotArrangementStrategy : ConnectedArrangementStrategy
{
	[Export] public bool RequireEverySlot { get; set; } = true;

	protected override double CandidateWeight(RoomRegion room, RoomArrangementDefinition definition,
		HashSet<Vector2I> floor, Vector2I anchor, int turn, Vector2I[] occupied, bool atWall) =>
		room.FurnishingSlots.Any(slot => slot.Definition.Id == definition.Id && slot.Anchor == anchor && slot.QuarterTurns == turn) ? 1 : 0;

	protected override Transform3D CandidateTransform(RoomRegion room, RoomArrangementDefinition definition,
		Vector2I anchor, int turn, Transform3D defaultTransform) => room.FurnishingSlots.Single(slot =>
			slot.Definition.Id == definition.Id && slot.Anchor == anchor && slot.QuarterTurns == turn).Transform;

	public override List<RoomArrangementPlacement> Plan(MapData map, IReadOnlyList<RoomRegion> rooms,
		RoomArrangementPool pool, ulong seed, int attempt, Func<Vector2I, Vector3> tileCenter, IReadOnlyList<Aabb> obstacles, IReadOnlyList<Aabb> walls, IReadOnlyList<Aabb> passageObstacles = null)
	{
		var result = base.Plan(map, rooms, pool, seed, attempt, tileCenter, obstacles, walls, passageObstacles);
		if (!RequireEverySlot) return result;
		foreach (var room in rooms.Where(room => room.IsProcedural))
		foreach (var slot in room.FurnishingSlots)
			if (!result.Any(p => p.RoomId == room.Id && p.Definition.Id == slot.Definition.Id && p.Anchor == slot.Anchor && p.QuarterTurns == slot.QuarterTurns))
				throw new InvalidOperationException($"Room {room.Id}: required furnishing {slot.Definition.Id} at {slot.Anchor} cannot fit safely.");
		return result;
	}
}