using System.Collections.Generic;
using Godot;

/// <summary>A base material broken by clumped patches of another: dirt through stone, worn rooms, mixed floors.</summary>
[Tool, GlobalClass]
public partial class PatchedFloor : FloorStyle
{
	[Export] public FloorMaterial Base { get; set; }
	[Export] public FloorMaterial Patch { get; set; }
	/// <summary>Approximate share of the floor the patches cover.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float Coverage { get; set; } = .3f;
	/// <summary>Patch size in quarter tiles; larger values give fewer, bigger patches.</summary>
	[Export(PropertyHint.Range, "1,12,0.5")] public float PatchSize { get; set; } = 4;

	public override IEnumerable<FloorMaterial> Materials => [Base, Patch];

	protected override void Assign(RoomBlueprint room, FloorMaterial[,] quads, RandomNumberGenerator rng)
	{
		var noise = new FastNoiseLite { Seed = (int)rng.Randi(), Frequency = 1f / PatchSize, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex };
		// Noise is roughly uniform in [-1, 1]; this threshold leaves about Coverage of the quads above it.
		float threshold = 1 - 2 * Coverage;
		for (int x = 0; x < quads.GetLength(0); x++)
		for (int z = 0; z < quads.GetLength(1); z++)
			quads[x, z] = noise.GetNoise2D(x, z) * 1.6f > threshold ? Patch : Base;
	}
}
