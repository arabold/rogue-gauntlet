using System.Linq;
using Godot;

/// <summary>Selects from an assigned storage palette along the perimeter, leaving the center clear.</summary>
[Tool, GlobalClass]
public partial class CellarRoomGenerator : RoomGenerator
{
	[Export] public RoomArrangementDefinition[] StorageArrangements { get; set; } = [];
	[Export(PropertyHint.Range, "1,10")] public int MaximumClusters { get; set; } = 8;
	[Export(PropertyHint.Range, "0.01,0.5,0.01")] public float MaximumOccupiedFraction { get; set; } = .4f;
	[Export(PropertyHint.Range, "0.75,1.5,0.05")] public float PassageRadius { get; set; } = 1;

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		if (ShellBuilder != null && (ShellBuilder.ShapeArchetype != RoomShapeArchetype.Rectangle
			|| ShellBuilder.Purpose != RoomPurpose.Chamber || ShellBuilder.ColumnChance != 0 || ShellBuilder.EnableChasms))
			errors.Add("Cellar: assign a rectangular Chamber shell with columns and chasms disabled.");
		if (StorageArrangements == null || StorageArrangements.Length == 0 || StorageArrangements.Any(entry => entry == null || entry.Placement != ArrangementPlacement.Floor))
			errors.Add("Cellar: assign a palette of floor storage arrangements.");
		return errors.ToArray();
	}

	public override RoomArrangementPool CreateInteriorPool()
	{
		if (StorageArrangements == null || StorageArrangements.Length == 0 || StorageArrangements.Any(entry => entry == null)) return null;
		var storage = StorageArrangements.Select(entry =>
		{
			var copy = (RoomArrangementDefinition)entry.Duplicate();
			copy.MaximumPerRoom = MaximumClusters;
			return copy;
		}).ToArray();
		return new RoomArrangementPool
		{
			Entries = storage, PlacementStrategy = new PerimeterStorageStrategy(),
			MaximumPerRoom = MaximumClusters, MaximumWallPerRoom = 0,
			MaximumOccupiedFraction = MaximumOccupiedFraction, PassageRadius = PassageRadius,
		};
	}
}