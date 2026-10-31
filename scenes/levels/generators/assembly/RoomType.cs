using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Where a room type puts its entrances.</summary>
public enum DoorwayPlacement { Any, LongAxisEnds, LongSides, SingleEnd }

/// <summary>What floor area a room type keeps clear of furniture before furnishing.</summary>
public enum CirculationMode { DoorwayApproaches, DoorToDoorSpine, None }

/// <summary>
/// One kind of room, composed from a strategy (or weighted palette of strategies) per axis: shape and size, doorways,
/// wall style, floor style, columns and an ordered list of furnishing steps. <see cref="RoomAssembler"/> runs them.
/// New behaviour is a new strategy Resource, never a new room-type class.
/// </summary>
[Tool, GlobalClass]
public partial class RoomType : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;

	[ExportGroup("Structure")]
	[Export] public RoomShape[] Shapes { get; set; } = [];
	[Export(PropertyHint.Range, "1,144")] public int MinimumFloorTiles { get; set; } = 1;
	[Export(PropertyHint.Range, "1,4")] public int MinDoorways { get; set; } = 1;
	[Export(PropertyHint.Range, "1,4")] public int MaxDoorways { get; set; } = 3;
	[Export] public DoorwayPlacement DoorwayPlacement { get; set; }
	[Export] public PackedScene DoorScene { get; set; }
	[Export(PropertyHint.Range, "0,1,0.05")] public float DoorChance { get; set; }
	[Export] public CirculationMode Circulation { get; set; }
	/// <summary>Largest share of standable floor that may end up inaccessible. Small pockets behind furniture read naturally;
	/// sealing off half a room does not. Nothing ever spawns in a pocket either way.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float MaxPocketShare { get; set; } = .1f;

	[ExportGroup("Surfaces")]
	[Export] public WallStyle[] Walls { get; set; } = [];
	[Export] public FloorStyle[] Floors { get; set; } = [];

	[ExportGroup("Content")]
	[Export] public ColumnPattern[] Columns { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.05")] public float ColumnChance { get; set; }
	/// <summary>Run in order against the same room; earlier steps get first choice of space.</summary>
	[Export] public FurnishingStep[] Steps { get; set; } = [];
	/// <summary>Shape rerolls allowed when a required step cannot fit.</summary>
	[Export(PropertyHint.Range, "1,16")] public int Attempts { get; set; } = 8;

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("Room type: assign a stable ID.");
		if (Shapes.Length == 0 || Shapes.Any(shape => shape == null)) errors.Add($"Room type {Id}: assign shapes.");
		if (Walls.Length == 0 || Walls.Any(style => style == null)) errors.Add($"Room type {Id}: assign wall styles.");
		if (Floors.Length == 0 || Floors.Any(style => style == null)) errors.Add($"Room type {Id}: assign floor styles.");
		if (Columns.Any(column => column == null) || Steps.Any(step => step == null)) errors.Add($"Room type {Id}: remove empty column or step entries.");
		if (MinDoorways > MaxDoorways) errors.Add($"Room type {Id}: the doorway minimum exceeds the maximum.");
		if (DoorChance > 0 && DoorScene == null) errors.Add($"Room type {Id}: assign a door scene or set the door chance to zero.");
		errors.AddRange(Shapes.Where(shape => shape != null).SelectMany(shape => shape.Validate()));
		errors.AddRange(Walls.Where(style => style != null).SelectMany(style => style.Validate()));
		errors.AddRange(Floors.Where(style => style != null).SelectMany(style => style.Validate()));
		errors.AddRange(Columns.Where(column => column != null).SelectMany(column => column.Validate()));
		errors.AddRange(Steps.Where(step => step != null).SelectMany(step => step.Validate()));
		return errors.Distinct().ToArray();
	}
}