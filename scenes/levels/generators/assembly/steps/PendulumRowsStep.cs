using Godot;

/// <summary>
/// Gauntlet rows of pendulum blades swinging across the room: by default one over every tile column, neighbours half a
/// beat apart, so the gaps between them open and close like scissors; with <see cref="OnePerRow"/> a single blade over
/// the middle column that swings from wall to wall. Each blade's beat carries the space it has, to the wall or halfway
/// to the next blade (which swings toward it at the same time), and the blade narrows its sweep to fit.
/// </summary>
[Tool, GlobalClass]
public partial class PendulumRowsStep : HazardRowsStep
{
	/// <summary>Space a blade keeps to a wall, and half the space it keeps to a neighbouring blade, at its closest.</summary>
	private const float BladeClearance = .1f;

	/// <summary>The least space either side of a blade's middle worth a swing; a row with less places nothing.</summary>
	[Export(PropertyHint.Range, "0.5,4,0.05,suffix:m")] public float MinimumSpace { get; set; } = 1.2f;
	/// <summary>One blade per row, over the middle column, with the whole row's width to swing in: its content needs a
	/// sweep wide enough to reach both walls, or the row could be walked around.</summary>
	[Export] public bool OnePerRow { get; set; }

	protected override PropMount Mount => PropMount.Floor;

	protected override bool LayRow(RoomBlueprint room, RandomNumberGenerator rng, int row, int[] columns, int index, HazardBeat beat)
	{
		var choice = Choose(room, rng);
		if (choice == null) return false;
		if (OnePerRow) columns = [columns[columns.Length / 2]];
		var plan = new PlacementPlan();
		var spaces = new float[columns.Length];
		for (int column = 0; column < columns.Length; column++)
		{
			var tile = new Vector2I(columns[column], row);
			spaces[column] = Mathf.Min(SpaceToward(room, tile, Vector2I.Left), SpaceToward(room, tile, Vector2I.Right));
			if (spaces[column] < MinimumSpace) return false;
			choice.Compose(room, rng, plan, new Transform3D(Basis.Identity, RoomFootprint.TileCenter(tile)), optional: false);
		}
		int first = room.Pieces.Count;
		if (!TryPlace(room, choice, plan, Transform3D.Identity)) return false;
		for (int column = 0; column < columns.Length; column++)
		{
			float phase = OnePerRow ? beat.Phase : Mathf.PosMod(beat.Phase + columns[column] % 2 * .5f, 1f);
			room.AssignBeat(first + column, beat with { Phase = phase, Space = spaces[column] });
		}
		return true;
	}

	/// <summary>Space from a blade's middle toward <paramref name="side"/>: to just short of the wall, or halfway to the
	/// next column's blade. A lone blade has every floor tile up to the wall.</summary>
	private float SpaceToward(RoomBlueprint room, Vector2I tile, Vector2I side)
	{
		float space = Room.TileSize / 2f - BladeClearance;
		if (!OnePerRow) return space - (room.Footprint.IsFloor(tile + side) ? 0 : room.WallDepth(tile, side));
		for (; room.Footprint.IsFloor(tile + side); tile += side) space += Room.TileSize;
		return space - room.WallDepth(tile, side);
	}
}
