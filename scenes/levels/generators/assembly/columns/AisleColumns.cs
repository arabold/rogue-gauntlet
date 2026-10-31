using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Two rows of columns flanking a central aisle along the room's length, in mirrored pairs.</summary>
[Tool, GlobalClass]
public partial class AisleColumns : ColumnPattern
{
	[Export(PropertyHint.Range, "1,4")] public int Spacing { get; set; } = 2;

	protected override IEnumerable<Vector3[]> Groups(RoomFootprint footprint, RandomNumberGenerator rng)
	{
		if (footprint.Width < 3) yield break;
		// Odd widths flank the center tile; even widths keep one tile either side of the center line.
		int left = footprint.Width % 2 == 1 ? (footprint.Width - 1) / 2 : footprint.Width / 2 - 1;
		int right = footprint.Width % 2 == 1 ? left + 1 : footprint.Width / 2 + 1;
		foreach (int z in Spaced(footprint.Length, Spacing))
		{
			var pair = Corners(footprint, new(left, z), new(right, z));
			if (pair.Length > 0) yield return pair;
		}
	}
}
