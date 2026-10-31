using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Fill the generator's exact slots using the common physical checks; incomplete room compositions fail explicitly.</summary>
[Tool, GlobalClass]
public partial class SlotArrangementStrategy : RoomArrangementPlacementStrategy
{
	[Export] public bool RequireEverySlot { get; set; } = true;

	public override List<RoomArrangementPlacement> Plan(MapData map, IReadOnlyList<RoomRegion> rooms,
		RoomArrangementPool pool, ulong seed, int attempt, Func<Vector2I, Vector3> tileCenter, IReadOnlyList<Aabb> obstacles,
		IReadOnlyList<Aabb> walls, IReadOnlyList<Aabb> passageObstacles = null)
	{
		var errors = pool.Validate();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
		var result = new List<RoomArrangementPlacement>();
		var occupied = obstacles.ToList();
		var blocking = (passageObstacles ?? obstacles).ToList();
		foreach (var room in rooms.Where(room => room.IsProcedural))
		{
			if (room.FurnishingSlots.Any(slot => string.IsNullOrWhiteSpace(slot.SlotId))
				|| room.FurnishingSlots.Select(slot => slot.SlotId).Distinct().Count() != room.FurnishingSlots.Count)
				throw new InvalidOperationException($"Room {room.Id}: required slot IDs must be nonempty and unique.");
			var validator = new ArrangementPlacementValidator(map, room, tileCenter, occupied, walls, blocking, pool.PassageRadius);
			var repeats = new Dictionary<string, int>();
			foreach (var slot in room.FurnishingSlots)
			{
				var definition = pool.Entries.SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).SingleOrDefault(entry => entry.Id == slot.Definition.Id);
				var transform = slot.Transform;
				// Slots retain resolved mounts for editing. Reconstruct their nominal normal/height before
				// checking support, preserving the measured position along the wall through rotation/packing.
				if (definition != null && (definition.AlignToWall || definition.Placement == ArrangementPlacement.Wall))
				{
					var outward = ConnectedArrangementStrategy.Rotate(Vector2I.Up, slot.QuarterTurns);
					var normal = new Vector3(outward.X, 0, outward.Y);
					transform.Origin += normal * (tileCenter(slot.Anchor) - transform.Origin).Dot(normal);
					if (definition.Placement == ArrangementPlacement.Wall) transform.Origin = new(transform.Origin.X, tileCenter(slot.Anchor).Y, transform.Origin.Z);
				}
				if (definition != null && result.Count(p => p.RoomId == room.Id && p.Definition.Placement == definition.Placement)
						< (definition.Placement == ArrangementPlacement.Wall ? pool.MaximumWallPerRoom : pool.MaximumPerRoom)
					&& repeats.GetValueOrDefault(definition.Id) < definition.MaximumPerRoom
					&& validator.TryPlace(definition, slot.Anchor, slot.QuarterTurns, transform, out var placement, checkPassage: !RequireEverySlot))
				{
					placement = placement with { SlotId = slot.SlotId };
					validator.Accept(placement);
					result.Add(placement);
					repeats[definition.Id] = repeats.GetValueOrDefault(definition.Id) + 1;
				}
				else if (RequireEverySlot)
					throw new InvalidOperationException($"Room {room.Id}: required furnishing {slot.SlotId} ({slot.Definition.Id}) cannot fit safely: {validator.LastFailure}.");
			}
			if (RequireEverySlot && !validator.IsConnected)
				throw new InvalidOperationException($"Room {room.Id}: required furnishing composition disconnects free space.");
		}
		return result;
	}
}