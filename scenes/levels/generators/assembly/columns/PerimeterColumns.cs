using System.Collections.Generic;
using Godot;

/// <summary>A colonnade one tile in from the walls, in mirrored pairs across the room.</summary>
[Tool, GlobalClass]
public partial class PerimeterColumns : ColumnPattern
{
	[Export(PropertyHint.Range, "1,4")] public int Spacing { get; set; } = 2;

	protected override IEnumerable<Vector3[]> Groups(RoomFootprint footprint, RandomNumberGenerator rng)
	{
		if (footprint.Width < 3 || footprint.Length < 3) yield break;
		int right = footprint.Width - 1, back = footprint.Length - 1;
		foreach (int z in Spaced(footprint.Length, Spacing))
		{
			var pair = Corners(footprint, new(1, z), new(right, z));
			if (pair.Length > 0) yield return pair;
		}
		foreach (int x in Spaced(footprint.Width, Spacing))
		{
			if (x == 1 || x == right) continue;
			var pair = Corners(footprint, new(x, 1), new(x, back));
			if (pair.Length > 0) yield return pair;
		}
	}
}
