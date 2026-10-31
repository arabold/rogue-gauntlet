using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Composes shared footprint, furnishing and optional column policies. All mutable plans belong to a candidate room.</summary>
[Tool, GlobalClass]
public partial class ModularRoomGenerator : RoomGenerator
{
	[Export(PropertyHint.Range, "1,144")] public int MinimumFloorTiles { get; set; } = 1;
	[Export] public RoomLayoutVariant[] Layouts { get; set; } = [];
	[Export] public RoomColumnLayout Columns { get; set; }
	[Export(PropertyHint.Range, "0,1")] public float ColumnChance { get; set; } = .5f;
	[Export(PropertyHint.Range, "1,16")] public int CompositionAttempts { get; set; } = 8;

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		if (ShellBuilder != null && (ShellBuilder.Purpose != RoomPurpose.Chamber || ShellBuilder.ArrangementPool != null))
			errors.Add("Modular room: assign an unfurnished Chamber shell; variants own shape, furniture and columns.");
		if (Layouts == null || Layouts.Length == 0 || Layouts.Any(layout => layout == null))
			errors.Add("Modular room: assign a nonempty layout palette.");
		else
		{
			foreach (var layout in Layouts)
			{
				errors.AddRange(layout.Validate());
				if (layout.BaseLayout != null && !layout.BaseLayout.EligibleSizes(MinimumFloorTiles).Any())
					errors.Add($"Layout {layout.Id}: size bounds cannot meet the minimum floor-cell count.");
			}
			if (Layouts.Select(layout => layout.Id).Distinct().Count() != Layouts.Length) errors.Add("Modular room: layout IDs must be unique.");
		}
		if (MinimumFloorTiles < 1 || MinimumFloorTiles > 144) errors.Add("Modular room: choose a minimum floor-cell count from 1 to 144.");
		if (!float.IsFinite(ColumnChance) || ColumnChance < 0 || ColumnChance > 1 || CompositionAttempts < 1 || CompositionAttempts > 16)
			errors.Add("Modular room: assign a column probability and bounded composition attempts.");
		errors.AddRange(Columns?.Validate(ShellBuilder?.DecorationLibrary) ?? []);
		return errors.ToArray();
	}

	public override RoomArrangementPool CreateInteriorPool()
	{
		if (Layouts == null || Layouts.Length == 0 || Layouts.Any(layout => layout?.Contents?.Definitions == null)) return null;
		int maximum = Layouts.Max(layout => layout.Contents.MaximumCount);
		return CreateSlotPool(maximum, maximum,
			Layouts.SelectMany(layout => layout.Contents.Definitions).DistinctBy(definition => definition?.Id).ToArray());
	}

	public override RoomArrangementPool CreateInteriorPool(Room room)
	{
		var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
		return CreateSlotPool(slots.Count(slot => slot.Definition.Placement == ArrangementPlacement.Floor),
			slots.Count(slot => slot.Definition.Placement == ArrangementPlacement.Wall), slots.Select(slot => slot.Definition).DistinctBy(definition => definition.Id).ToArray());
	}

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		// Fixed parent draws per room. Attempts, furniture and columns each have their own continuation.
		ulong seed = ((ulong)rng.Randi() << 32) | rng.Randi();
		using var selection = GenerationRandom.Create(seed, GenerationRandom.Layout, "room-variant");
		double roll = selection.Randf() * Layouts.Sum(layout => layout.Weight);
		var variant = Layouts[^1];
		foreach (var choice in Layouts) { roll -= choice.Weight; if (roll < 0) { variant = choice; break; } }
		for (int attempt = 0; attempt < CompositionAttempts; attempt++)
		{
			using var structure = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{variant.Id}:attempt:{attempt}");
			if (!variant.BaseLayout.Populate(room, ShellBuilder, structure, MinimumFloorTiles)) return false;
			if (RoomContentLayout.Describe(room).Tiles.Count < MinimumFloorTiles) continue;
			ulong contentSeed = GenerationRandom.DeriveSeed(seed, GenerationRandom.Arrangements, variant.Id);
			if (!variant.Contents.TryPlan(room, contentSeed, 1, out var contents)) continue;
			AddPlannedSlots(room, contents, variant.Id);
			using var columns = GenerationRandom.Create(seed, GenerationRandom.Layout, $"{variant.Id}:columns");
			if (Columns != null && columns.Randf() < ColumnChance) Columns.Populate(room, contents, columns, 1);
			room.SetMeta("room_layout_variant", variant.Id);
			room.SetMeta("room_shape", (int)variant.BaseLayout.Shape);
			// Retain the perimeter used to validate required furniture. The floor generator fills corridor walls
			// around these explicit openings without substituting more intrusive wall/pillar variants.
			room.BakeTileMap();
			return true;
		}
		throw new InvalidOperationException($"Modular room {ResourcePath}: layout {variant.Id} cannot fit its required contents after {CompositionAttempts} attempts.");
	}
}