using System.Collections.Generic;
using Godot;

/// <summary>A single central column, or four columns framing the center tile.</summary>
[Tool, GlobalClass]
public partial class CenterColumns : ColumnPattern
{
	[Export] public bool Cluster { get; set; }

	protected override IEnumerable<Vector3[]> Groups(RoomFootprint footprint, RandomNumberGenerator rng)
	{
		int x = footprint.Width / 2, z = footprint.Length / 2;
		var group = Cluster
			? Corners(footprint, new(x, z), new(x + 1, z), new(x, z + 1), new(x + 1, z + 1))
			: Corners(footprint, new Vector2I((footprint.Width + 1) / 2, (footprint.Length + 1) / 2));
		if (group.Length > 0) yield return group;
	}
}
