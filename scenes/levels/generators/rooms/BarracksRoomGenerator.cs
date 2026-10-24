using System.Linq;
using Godot;

/// <summary>Opposing bed pairs face a continuous central passage. More pairs extend the room by fixed bays.</summary>
[Tool, GlobalClass]
public partial class BarracksRoomGenerator : RoomGenerator
{
	[Export] public RoomArrangementDefinition BedArrangement { get; set; }
	[Export(PropertyHint.Range, "3,5,2")] public int WidthTiles { get; set; } = 3;
	[Export] public int[] BedPairs { get; set; } = [2, 3, 4];

	public override RoomArrangementPool CreateInteriorPool() => CreateSlotPool(BedArrangement);

	public override string[] Validate()
	{
		var errors = base.Validate().Concat(ValidateSlotShell()).ToList();
		if (WidthTiles != 3 && WidthTiles != 5) errors.Add("Barracks: choose a width of three or five tiles.");
		if (BedPairs == null || BedPairs.Length == 0 || BedPairs.Any(pairs => pairs < 1 || pairs > 4)) errors.Add("Barracks: choose one to four bed pairs.");
		if (BedArrangement != null && (BedArrangement.Placement != ArrangementPlacement.Floor || BedArrangement.OccupiedTiles == null || !BedArrangement.OccupiedTiles.SequenceEqual(new[] { Vector2I.Zero }))) errors.Add("Barracks: assign a single-tile bed composition.");
		return errors.ToArray();
	}

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		int pairs = BedPairs[rng.RandiRange(0, BedPairs.Length - 1)];
		if (!PopulateSlotShell(room, rng, standalone, new(WidthTiles, pairs * 2 + 1))) return false;
		for (int row = 0; row < pairs; row++)
		{
			AddSlot(room, BedArrangement, new(0, row * 2 + 1), 1, Vector3.Right * .6f);
			AddSlot(room, BedArrangement, new(WidthTiles - 1, row * 2 + 1), 3, Vector3.Left * .6f);
		}
		return true;
	}
}