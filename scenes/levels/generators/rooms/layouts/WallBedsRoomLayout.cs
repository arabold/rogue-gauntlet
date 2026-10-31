using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Furnish eligible wall spans using the same individual bed-cluster operation, independently of the footprint.</summary>
[Tool, GlobalClass]
public partial class WallBedsRoomLayout : RoomContentLayout
{
	[Export] public BedClusterDefinition Cluster { get; set; }
	[Export(PropertyHint.Flags, "North,East,South,West")] public int AllowedWalls { get; set; } = 15;
	[Export(PropertyHint.Range, "1,16")] public int MinimumBeds { get; set; } = 2;
	[Export(PropertyHint.Range, "1,16")] public int MaximumBeds { get; set; } = 4;
	[Export(PropertyHint.Range, "0,2,0.05")] public float Spacing { get; set; } = .35f;
	[Export(PropertyHint.Range, "0,2,0.05")] public float EndClearance { get; set; } = .6f;
	public override RoomArrangementDefinition[] Definitions => Cluster?.Definitions ?? [];
	public override int MaximumCount => MaximumBeds * (Cluster?.MaximumPieces ?? 0);

	public override string[] Validate()
	{
		var errors = (Cluster?.Validate() ?? ["Wall beds: assign a bed cluster."]).ToList();
		if (AllowedWalls <= 0 || (AllowedWalls & ~15) != 0 || MinimumBeds < 1 || MaximumBeds < MinimumBeds || MaximumBeds > 16
			|| !float.IsFinite(Spacing) || Spacing < 0 || Spacing > 2 || !float.IsFinite(EndClearance) || EndClearance < 0 || EndClearance > 2)
			errors.Add("Wall beds: assign wall directions, bed limits, spacing and end clearance.");
		return errors.ToArray();
	}

	public override bool Populate(RoomFurnishingContext context, string key)
	{
		using var rng = context.Random(key);
		var walls = context.WallSpans().Where(wall => (AllowedWalls & (int)DoorwayMarker.GetDirectionFlag(wall.Outward)) != 0)
			.Select(wall => (Wall: wall, Order: rng.Randi())).OrderBy(entry => entry.Order).Select(entry => entry.Wall).ToArray();
		int beds = 0;
		foreach (var wall in walls)
		{
			beds += RoomFurnishingPatterns.PlaceBedsAlongWall(context, Cluster, wall, Spacing, EndClearance, MaximumBeds - beds,
				$"{key}:wall:{wall.Start}:{wall.Outward}");
			if (beds >= MaximumBeds) break;
		}
		return beds >= MinimumBeds;
	}
}