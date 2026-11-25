using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>What a <see cref="HazardStep"/> lays across a room.</summary>
public enum HazardLayout
{
	/// <summary>Wall spouts on the long walls, firing across the room.</summary>
	CrossFire,
	/// <summary>A pendulum blade over every tile column, swinging across the room; neighbours swing in opposite directions,
	/// so the gaps between them open and close like scissors.</summary>
	PendulumRows
}

/// <summary>Which long wall a cross-fire row's spout hangs on.</summary>
public enum SpoutSides { Alternate, Both, Left, Right }

/// <summary>
/// The rows of a trap gauntlet across a room's length (+Z, the door-to-door spine): one row every
/// <see cref="RowSpacing"/> tiles, clear of the doorway tiles at both ends, on one beat that <see cref="Pattern"/>
/// staggers along +Z. A row covers the room's whole width, so the gauntlet cannot be walked around, and its band is
/// reserved, so later furniture never blocks a lane or a swing. A row stands whole or not at all. Put it first. <see cref="FurnishingStep.MinimumCount"/>
/// and <see cref="FurnishingStep.MaximumCount"/> count rows.
/// </summary>
[Tool, GlobalClass]
public partial class HazardStep : FurnishingStep
{
	/// <summary>Depth of the band around each row's line kept free of furniture.</summary>
	private const float RowBand = 2f;
	/// <summary>Space a blade keeps to a wall, and half the space it keeps to a neighbouring blade, at its closest.</summary>
	private const float BladeClearance = .1f;

	[Export] public HazardLayout Layout { get; set; }
	/// <summary>How rows are staggered, read from the room's start (-Z end) toward its far end.</summary>
	[Export] public HazardPattern Pattern { get; set; } = HazardPattern.Wave;
	/// <summary>Cross-fire rows: the wall each row's spout hangs on.</summary>
	[Export] public SpoutSides Sides { get; set; } = SpoutSides.Alternate;
	/// <summary>Seconds per beat, drawn once per room.</summary>
	[Export] public Vector2 PeriodRange { get; set; } = new(2f, 2.6f);
	[Export(PropertyHint.Range, "1,6")] public int RowSpacing { get; set; } = 1;
	/// <summary>Tiles kept free of rows at each end, where the doorways and their approaches are.</summary>
	[Export(PropertyHint.Range, "1,3")] public int EndMargin { get; set; } = 1;
	/// <summary>Pendulum rows: the farthest a blade swings to each side, before walls and neighbours narrow it.</summary>
	[Export(PropertyHint.Range, "0.2,4,0.05,suffix:m")] public float MaximumSweep { get; set; } = 1.9f;
	/// <summary>Pendulum rows: the narrowest swing worth placing; a row with less room places nothing.</summary>
	[Export(PropertyHint.Range, "0.1,2,0.05,suffix:m")] public float MinimumSweep { get; set; } = .4f;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var footprint = room.Footprint;
		float period = rng.RandfRange(PeriodRange.X, PeriodRange.Y);
		int placed = 0, first = -1;
		for (int row = EndMargin; row < footprint.Length - EndMargin && placed < MaximumCount; row += RowSpacing)
		{
			var columns = Enumerable.Range(0, footprint.Width).Where(column => footprint.IsFloor(column, row)).ToArray();
			if (columns.Length == 0) continue;
			if (first < 0) first = row;
			float phase = TrapGauntlet.PhaseFor(Pattern, placed, (row - first) * Room.TileSize, period, TrapGauntlet.HeroWalkSpeed);
			int index = placed, at = row;
			bool laid = room.TryAtomically(() => Layout == HazardLayout.CrossFire
				? LayCrossFire(room, rng, at, columns, index, new HazardBeat(period, phase))
				: LayPendulums(room, rng, at, columns, period, phase));
			if (!laid) continue;
			float z = RoomFootprint.TileCenter(new Vector2I(0, row)).Z;
			room.Reserve(new Rect2(room.Interior.Position.X, z - RowBand / 2, room.Interior.Size.X, RowBand));
			placed++;
		}
		return placed;
	}

	public override string[] Validate()
	{
		var errors = base.Validate().ToList();
		var props = Content.OfType<Prop>().ToArray();
		if (props.Length != Content.Length) errors.Add($"Step {Id}: hazard content must be single props, one hazard scene each.");
		var mount = Layout == HazardLayout.CrossFire ? PropMount.Wall : PropMount.Floor;
		if (props.Any(prop => prop.Mount != mount || prop.Blocking || prop.Scenes.Length == 0))
			errors.Add($"Step {Id}: {Layout} needs non-blocking {mount.ToString().ToLowerInvariant()} props with hazard scenes.");
		if (PeriodRange.X <= 0 || PeriodRange.Y < PeriodRange.X) errors.Add($"Step {Id}: use an ordered, positive period range.");
		if (MinimumSweep > MaximumSweep) errors.Add($"Step {Id}: the minimum sweep exceeds the maximum.");
		return errors.ToArray();
	}

	/// <summary>
	/// One spout per chosen side wall at <paramref name="row"/>, all firing across the room on one beat. A one-sided row
	/// whose wall has no plain stretch there (an alcove, a pillar) hangs its spout on the opposite wall instead.
	/// </summary>
	private bool LayCrossFire(RoomBlueprint room, RandomNumberGenerator rng, int row, int[] columns, int index, HazardBeat beat)
	{
		var left = (new Vector2I(columns[0], row), Vector2I.Left);
		var right = (new Vector2I(columns[^1], row), Vector2I.Right);
		bool leftFirst = Sides == SpoutSides.Left || Sides == SpoutSides.Alternate && index % 2 == 0;
		int before = room.Pieces.Count;
		bool hung = Sides == SpoutSides.Both
			? HangSpout(room, rng, left) && HangSpout(room, rng, right)
			: HangSpout(room, rng, leftFirst ? left : right) || HangSpout(room, rng, leftFirst ? right : left);
		if (hung) room.AssignBeat(before, beat);
		return hung;
	}

	/// <summary>Hangs one spout centred on <paramref name="tile"/>'s wall facing <paramref name="outward"/>.</summary>
	private bool HangSpout(RoomBlueprint room, RandomNumberGenerator rng, (Vector2I Tile, Vector2I Outward) wall)
	{
		var (tile, outward) = wall;
		foreach (var run in room.WallRuns.Where(run => run.Outward == outward))
		{
			var offset = tile - run.Start;
			int index = offset.X * run.Along.X + offset.Y * run.Along.Y;
			if (index < 0 || index >= run.Length || run.TileAt(index) != tile) continue;
			float middle = run.TileCenterAlong(index);
			foreach (float center in run.MountCenters(index, room.DecorMounts).OrderBy(center => Mathf.Abs(center - middle)))
			{
				var plan = Next(room, rng, out var choice);
				if (plan.IsEmpty) return false;
				if (TryPlaceAgainstWall(room, choice, plan, run, center - room.WidthAlong(plan, run) / 2)) return true;
			}
			return false;
		}
		return false;
	}

	/// <summary>
	/// One blade over each of <paramref name="columns"/> at <paramref name="row"/>. Each swings as far as its walls and its
	/// neighbours allow, up to <see cref="MaximumSweep"/>; neighbours swing half a period apart.
	/// </summary>
	private bool LayPendulums(RoomBlueprint room, RandomNumberGenerator rng, int row, int[] columns, float period, float phase)
	{
		float halfWidth = -1;
		foreach (int column in columns)
		{
			var tile = new Vector2I(column, row);
			var plan = Next(room, rng, out var choice);
			if (plan.IsEmpty) return false;
			if (halfWidth < 0) halfWidth = BladeHalfWidth(plan.Pieces[0]);
			float sweep = Mathf.Min(MaximumSweep, Mathf.Min(SwingSpace(room, tile, Vector2I.Left, halfWidth), SwingSpace(room, tile, Vector2I.Right, halfWidth)));
			int pieces = room.Pieces.Count;
			if (sweep < MinimumSweep || !TryPlace(room, choice, plan, new Transform3D(Basis.Identity, RoomFootprint.TileCenter(tile)))) return false;
			room.AssignBeat(pieces, new HazardBeat(period, Mathf.PosMod(phase + column % 2 * .5f, 1f), sweep));
		}
		return true;
	}

	/// <summary>
	/// How far a blade over <paramref name="tile"/> may swing toward <paramref name="side"/>: to just short of the wall, or
	/// halfway to the next column's blade, which swings toward it at the same time.
	/// </summary>
	private static float SwingSpace(RoomBlueprint room, Vector2I tile, Vector2I side, float halfWidth)
	{
		float half = Room.TileSize / 2f;
		return room.Footprint.IsFloor(tile + side)
			? half - halfWidth - BladeClearance
			: half - room.WallDepth(tile, side) - halfWidth - BladeClearance;
	}

	private static float BladeHalfWidth(PlannedPiece piece)
	{
		var node = piece.Prop.Scenes[piece.Variant].Instantiate();
		float halfWidth = node is PendulumBlade blade ? blade.BladeHalfWidth : 0;
		node.Free();
		return halfWidth;
	}
}
