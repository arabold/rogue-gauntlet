using System.Collections.Generic;
using Godot;

/// <summary>A border material along the walls around a center material, e.g. stone around a wooden floor.</summary>
[Tool, GlobalClass]
public partial class BorderedFloor : FloorStyle
{
	[Export] public FloorMaterial Border { get; set; }
	[Export] public FloorMaterial Center { get; set; }
	/// <summary>Border width in quarter tiles (two per tile).</summary>
	[Export(PropertyHint.Range, "1,4")] public int Width { get; set; } = 1;

	public override IEnumerable<FloorMaterial> Materials => [Border, Center];

	protected override void Assign(RoomBlueprint room, FloorMaterial[,] quads, FloorSplit[,] splits, RandomNumberGenerator rng)
	{
		for (int x = 0; x < quads.GetLength(0); x++)
		for (int z = 0; z < quads.GetLength(1); z++)
			quads[x, z] = NearWall(room.Footprint, x, z) ? Border : Center;
	}

	private bool NearWall(RoomFootprint footprint, int qx, int qz)
	{
		for (int dx = -Width; dx <= Width; dx++)
		for (int dz = -Width; dz <= Width; dz++)
		{
			int x = qx + dx, z = qz + dz;
			if (x < 0 || z < 0 || !footprint.IsFloor(x / 2, z / 2)) return true;
		}
		return false;
	}
}
