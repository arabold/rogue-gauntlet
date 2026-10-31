using System.Collections.Generic;
using Godot;

/// <summary>One material across the whole room.</summary>
[Tool, GlobalClass]
public partial class UniformFloor : FloorStyle
{
	[Export] public FloorMaterial Material { get; set; }

	public override IEnumerable<FloorMaterial> Materials => [Material];

	protected override void Assign(RoomBlueprint room, FloorMaterial[,] quads, FloorSplit[,] splits, RandomNumberGenerator rng)
	{
		for (int x = 0; x < quads.GetLength(0); x++)
		for (int z = 0; z < quads.GetLength(1); z++)
			quads[x, z] = Material;
	}
}
