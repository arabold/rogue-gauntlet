using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Required furnishing bays follow the actual footprint's boundary, including recessed walls of irregular rooms.</summary>
[Tool, GlobalClass]
public partial class PerimeterRoomLayout : RoomContentLayout
{
	[Export] public RoomArrangementDefinition[] Arrangements { get; set; } = [];
	[Export(PropertyHint.Range, "0,32")] public int MinimumCount { get; set; } = 3;
	[Export(PropertyHint.Range, "1,32")] public int Count { get; set; } = 6;
	[Export(PropertyHint.Range, "0,1,0.05")] public float WallOffset { get; set; } = .5f;
	public override RoomArrangementDefinition[] Definitions => Arrangements?.Where(entry => entry != null).SelectMany(entry => entry.Variants).DistinctBy(entry => entry.Id).ToArray() ?? [];
	public override int MaximumCount => Count;

	public override string[] Validate()
	{
		var errors = new List<string>();
		if (MinimumCount < 0 || Count < MinimumCount || Count > 32 || !float.IsFinite(WallOffset) || WallOffset < 0 || WallOffset > 1)
			errors.Add("Perimeter layout: assign valid minimum/count and wall offset.");
		if (Arrangements == null || Arrangements.Length == 0 || Arrangements.Any(entry => entry == null))
			errors.Add("Perimeter layout: assign a nonempty composition palette.");
		else
		{
			foreach (var entry in Arrangements) errors.AddRange(entry.Validate());
			if (Arrangements.Select(entry => entry.Id).Distinct().Count() != Arrangements.Length
				|| Arrangements.Any(entry => entry.Placement != ArrangementPlacement.Floor))
				errors.Add("Perimeter layout: use unique floor composition IDs.");
		}
		return errors.ToArray();
	}

	public override bool Populate(RoomFurnishingContext context, string key)
	{
		var entries = Definitions.Select(entry =>
		{
			var copy = (RoomArrangementDefinition)entry.Duplicate();
			copy.Variations = [];
			copy.MaximumPerRoom = Count;
			return copy;
		}).ToArray();
		using var pool = new RoomArrangementPool
		{
			Entries = entries, PlacementStrategy = new PerimeterStorageStrategy { WallOffset = WallOffset },
			MaximumPerRoom = Count, MaximumWallPerRoom = 0, MaximumOccupiedFraction = 1, PassageRadius = context.PassageRadius,
		};
		using var rng = context.Random(key);
		var placements = pool.PlacementStrategy.Plan(context.Room.Map, [context.Region], pool, rng.Seed, 0,
			context.Center, context.Occupied, context.Walls, context.Blocking);
		if (placements.Count < MinimumCount) return false;
		for (int index = 0; index < placements.Count; index++)
			context.Accept(placements[index] with
			{
				Definition = Definitions.Single(entry => entry.Id == placements[index].Definition.Id), SlotId = $"{key}:storage:{index}",
			});
		return true;
	}
}