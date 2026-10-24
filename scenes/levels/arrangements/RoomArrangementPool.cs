using Godot;
using System;
using System.Linq;

/// <summary>Authored optional furnishings and density limits; all selection/occupancy state belongs to one generation.</summary>
[Tool, GlobalClass]
public partial class RoomArrangementPool : Resource
{
	[Export] public RoomArrangementDefinition[] Entries { get; set; } = [];
	[Export] public RoomArrangementPlacementStrategy PlacementStrategy { get; set; }
	/// <summary>Clearance for actor centers, matching the current navigation bake radius; conservatively expanded as a square.</summary>
	[Export(PropertyHint.Range, "0.75,1.5,0.05")] public float PassageRadius { get; set; } = 1.0f;
	/// <summary>Floor furnishing budget; elevated mounts have their own count and do not consume floor density.</summary>
	[Export] public int MaximumPerRoom { get; set; } = 2;
	[Export] public int MaximumWallPerRoom { get; set; } = 2;
	[Export(PropertyHint.Range, "0,1,0.01")] public float MaximumOccupiedFraction { get; set; } = .25f;

	public string[] Validate()
	{
		var errors = new System.Collections.Generic.List<string>();
		if (!float.IsFinite(PassageRadius) || PassageRadius < .75f || PassageRadius > 1.5f) errors.Add("Arrangement pool: passage radius must cover the hero (0.75–1.5).");
		if (PlacementStrategy == null) errors.Add("Arrangement pool: assign a placement strategy.");
		if (MaximumPerRoom < 0 || MaximumPerRoom > 64 || MaximumWallPerRoom < 0 || MaximumWallPerRoom > 64 || !float.IsFinite(MaximumOccupiedFraction)
			|| MaximumOccupiedFraction <= 0 || MaximumOccupiedFraction > 1) errors.Add("Arrangement pool: invalid density limits.");
		if (Entries == null || Entries.Length == 0 || Entries.Any(entry => entry == null)) errors.Add("Arrangement pool: assign nonempty entries.");
		else
		{
			foreach (var entry in Entries) errors.AddRange(entry.Validate());
			if (Entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != Entries.Length)
				errors.Add("Arrangement pool: IDs must be unique.");
		}
		return errors.ToArray();
	}
}