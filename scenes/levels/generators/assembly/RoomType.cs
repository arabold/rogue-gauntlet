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
	/// <summary>Realms (by ID) this room appears in; its styles and steps narrow down per realm by their own tags.</summary>
	[Export] public string[] Realms { get; set; } = [];

	[ExportGroup("Structure")]
	[Export] public RoomShape[] Shapes { get; set; } = [];
	[Export(PropertyHint.Range, "1,144")] public int MinimumFloorTiles { get; set; } = 1;
	[Export(PropertyHint.Range, "1,4")] public int MinDoorways { get; set; } = 1;
	[Export(PropertyHint.Range, "1,4")] public int MaxDoorways { get; set; } = 3;
	[Export] public DoorwayPlacement DoorwayPlacement { get; set; }
	/// <summary>Chance per non-secret doorway of the wall style's door; the rest get a frame or open walkway.
	/// Access locks only lock rooms whose doorways all have doors.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float DoorChance { get; set; }
	/// <summary>Independent chance per doorway to create a concealed door instead of the ordinary door/frame choice.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float SecretDoorChance { get; set; }
	/// <summary>Detection policy for room-owned secret doors; required when their chance is nonzero.</summary>
	[Export] public SecretDetectionProfile SecretDetection { get; set; }
	/// <summary>Chance per doorway without a door of the wall style's opening frame; the rest stay open walkways. The boundary fitter joins frames to adjacent walls.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float FrameChance { get; set; } = .5f;
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
	/// <summary>Seeded probability that each placed light fixture starts lit.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float FixtureLitChance { get; set; } = .85f;

	[Export(PropertyHint.Range, "1,16")] public int Attempts { get; set; } = 8;

	/// <summary>The wall styles a room in <paramref name="realm"/> draws from; null takes every style.</summary>
	public WallStyle[] WallsIn(string realm) => Walls.Where(style => Realm.Allows(style.Realms, realm)).ToArray();
	public FloorStyle[] FloorsIn(string realm) => Floors.Where(style => Realm.Allows(style.Realms, realm)).ToArray();
	public FurnishingStep[] StepsIn(string realm) => Steps.Where(step => Realm.Allows(step.Realms, realm)).ToArray();

	public string[] Validate()
	{
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(Id)) errors.Add("Room type: assign a stable ID.");
		if (Realms.Length == 0) errors.Add($"Room type {Id}: name the realms it appears in.");
		if (Shapes.Length == 0 || Shapes.Any(shape => shape == null)) errors.Add($"Room type {Id}: assign shapes.");
		if (Walls.Length == 0 || Walls.Any(style => style == null)) errors.Add($"Room type {Id}: assign wall styles.");
		if (Floors.Length == 0 || Floors.Any(style => style == null)) errors.Add($"Room type {Id}: assign floor styles.");
		if (Columns.Any(column => column == null) || Steps.Any(step => step == null)) errors.Add($"Room type {Id}: remove empty column or step entries.");
		if (!float.IsFinite(FixtureLitChance) || FixtureLitChance < 0 || FixtureLitChance > 1)
			errors.Add($"Room type {Id}: fixture lit chance must be between zero and one.");
		if (MinDoorways > MaxDoorways) errors.Add($"Room type {Id}: the doorway minimum exceeds the maximum.");
		if (DoorwayPlacement == DoorwayPlacement.LongAxisEnds && MinDoorways < 2)
			errors.Add($"Room type {Id}: long-axis ends need at least two doorways.");
		if (!float.IsFinite(SecretDoorChance) || SecretDoorChance < 0 || SecretDoorChance > 1)
			errors.Add($"Room type {Id}: secret door chance must be between zero and one.");
		if (SecretDoorChance > 0 && SecretDetection == null) errors.Add($"Room type {Id}: assign secret detection when secret doors are enabled.");
		if (DoorChance > 0 || SecretDoorChance > 0)
			foreach (var style in Walls.Where(style => style != null && style.DoorScene == null))
				errors.Add($"Room type {Id}: wall style {style.Id} has no door scene; assign one or disable both door chances.");
		if (Walls.All(style => style != null) && Floors.All(style => style != null))
			foreach (string realm in Realms.Where(realm => WallsIn(realm).Length == 0 || FloorsIn(realm).Length == 0))
				errors.Add($"Room type {Id}: no wall or floor style is tagged for realm '{realm}'.");
		errors.AddRange(Shapes.Where(shape => shape != null).SelectMany(shape => shape.Validate()));
		errors.AddRange(Walls.Where(style => style != null).SelectMany(style => style.Validate()));
		errors.AddRange(Floors.Where(style => style != null).SelectMany(style => style.Validate()));
		errors.AddRange(Columns.Where(column => column != null).SelectMany(column => column.Validate()));
		errors.AddRange(Steps.Where(step => step != null).SelectMany(step => step.Validate()));
		foreach (var step in Steps.Where(step => step != null))
		foreach (string wallId in step.WallStyles.Where(id => !Walls.Any(wall => wall != null && wall.Id == id)))
			errors.Add($"Room type {Id}: step {step.Id} references unknown wall style {wallId}.");
		return errors.Distinct().ToArray();
	}
}