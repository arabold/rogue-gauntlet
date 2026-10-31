using System.Collections.Generic;
using Godot;

/// <summary>Columns one tile in from each room corner, in mirrored pairs.</summary>
[Tool, GlobalClass]
public partial class CornerColumns : ColumnPattern
{
	protected override IEnumerable<Vector3[]> Groups(RoomFootprint footprint, RandomNumberGenerator rng)
	{
		int right = footprint.Width - 1, back = footprint.Length - 1;
		if (right < 2 || back < 2) yield break;
		foreach (int z in new[] { 1, back })
		{
			var pair = Corners(footprint, new(1, z), new(right, z));
			if (pair.Length > 0) yield return pair;
		}
	}
}
