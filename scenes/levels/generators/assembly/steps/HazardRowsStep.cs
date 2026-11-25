using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The rows of a trap gauntlet across a room's length (+Z, the door-to-door spine): one row every
/// <see cref="RowSpacing"/> tiles, clear of the doorway tiles at both ends, on one beat that <see cref="Pattern"/>
/// staggers along +Z. A row covers the room's whole width, so the gauntlet cannot be walked around, and its band is
/// reserved, so later furniture never blocks a lane or a swing. Each layout lays its own row; content is single hazard
/// props. Put it first. <see cref="FurnishingStep.MinimumCount"/> and <see cref="FurnishingStep.MaximumCount"/> count rows.
/// </summary>
[Tool, GlobalClass]
public abstract partial class HazardRowsStep : FurnishingStep
{
	/// <summary>Depth of the band around each row's line kept free of furniture.</summary>
	private const float RowBand = 2f;

	/// <summary>How rows are staggered, read from the room's start (-Z end) toward its far end.</summary>
	[Export] public HazardPattern Pattern { get; set; } = HazardPattern.Wave;
	/// <summary>Seconds per beat, drawn once per room.</summary>
	[Export] public Vector2 PeriodRange { get; set; } = new(2f, 2.6f);
	[Export(PropertyHint.Range, "1,6")] public int RowSpacing { get; set; } = 1;
	/// <summary>Tiles kept free of rows at each end, where the doorways and their approaches are.</summary>
	[Export(PropertyHint.Range, "1,3")] public int EndMargin { get; set; } = 1;

	/// <summary>Where this layout's hazard props mount.</summary>
	protected abstract PropMount Mount { get; }

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var footprint = room.Footprint;
		float period = rng.RandfRange(PeriodRange.X, PeriodRange.Y);
		int placed = 0;
		for (int row = EndMargin; row < footprint.Length - EndMargin && placed < MaximumCount; row += RowSpacing)
		{
			var columns = Enumerable.Range(0, footprint.Width).Where(column => footprint.IsFloor(column, row)).ToArray();
			if (columns.Length == 0) continue;
			if (!LayRow(room, rng, row, columns, placed, new HazardBeat(period, TrapGauntlet.PhaseFor(Pattern, placed))))
			{
				// A skipped row would leave a gap twice the spacing, so the rows stop at the first that does not fit. Too few
				// rows fail the step's minimum, and the room retries with another shape and walls.
				if (placed > 0) break;
				continue;
			}
			float z = RoomFootprint.TileCenter(new Vector2I(0, row)).Z;
			room.Reserve(new Rect2(room.Interior.Position.X, z - RowBand / 2, room.Interior.Size.X, RowBand));
			placed++;
		}
		return placed;
	}

	/// <summary>Lays the <paramref name="index"/>-th row at tile row <paramref name="row"/> over <paramref name="columns"/>
	/// on <paramref name="beat"/>; true when it stands.</summary>
	protected abstract bool LayRow(RoomBlueprint room, RandomNumberGenerator rng, int row, int[] columns, int index, HazardBeat beat);

	public override string[] Validate()
	{
		var errors = new List<string>(base.Validate());
		var props = Content.OfType<Prop>().ToArray();
		if (props.Length != Content.Length) errors.Add($"Step {Id}: hazard content must be single props, one hazard scene each.");
		if (props.Any(prop => prop.Mount != Mount || prop.Blocking || prop.Scenes.Length == 0))
			errors.Add($"Step {Id}: needs non-blocking {Mount.ToString().ToLowerInvariant()} props with hazard scenes.");
		if (PeriodRange.X <= 0 || PeriodRange.Y < PeriodRange.X) errors.Add($"Step {Id}: use an ordered, positive period range.");
		return errors.ToArray();
	}
}
