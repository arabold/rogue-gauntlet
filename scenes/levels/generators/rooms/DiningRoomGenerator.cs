using System.Linq;
using Godot;

/// <summary>Banquet rows determine room length, with circulation on both sides and entrances at each end.</summary>
[Tool, GlobalClass]
public partial class DiningRoomGenerator : RoomGenerator
{
	[Export] public RoomArrangementDefinition TableArrangement { get; set; }
	[Export] public int[] TableRows { get; set; } = [2, 3];

	public override RoomArrangementPool CreateInteriorPool() => CreateSlotPool(TableArrangement);

	public override string[] Validate()
	{
		var errors = base.Validate().Concat(ValidateSlotShell()).ToList();
		if (TableRows == null || TableRows.Length == 0 || TableRows.Any(rows => rows < 1 || rows > 4))
			errors.Add("Dining room: choose one to four banquet rows.");
		if (TableArrangement != null && (TableArrangement.Placement != ArrangementPlacement.Floor || TableArrangement.OccupiedTiles == null
			|| !TableArrangement.OccupiedTiles.SequenceEqual(new Vector2I[] { new(-1, 0), new(0, 0), new(1, 0) })))
			errors.Add("Dining room: assign a centered banquet composition reserving its center tile and its left/right neighbors.");
		return errors.ToArray();
	}

	protected override bool PopulateStructure(Room room, RandomNumberGenerator rng, bool standalone)
	{
		int rows = TableRows[rng.RandiRange(0, TableRows.Length - 1)];
		if (!PopulateSlotShell(room, rng, standalone, new(3, rows * 2 + 1))) return false;
		for (int row = 0; row < rows; row++) AddSlot(room, TableArrangement, new(1, row * 2 + 1));
		return true;
	}
}