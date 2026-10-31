using System.Collections.Generic;
using Godot;

/// <summary>
/// Footprint strategy: shape and size range in tiles. Width runs across the room (X), length along its axis (+Z).
/// Shapes return tight masks, so row 0 and column 0 always hold floor.
/// </summary>
[Tool, GlobalClass]
public abstract partial class RoomShape : Resource
{
	[Export(PropertyHint.Range, "0.01,10,0.01")] public float Weight { get; set; } = 1;
	/// <summary>Minimum width (X) and length (Y = along the room axis), in tiles.</summary>
	[Export] public Vector2I MinimumSize { get; set; } = new(3, 3);
	[Export] public Vector2I MaximumSize { get; set; } = new(5, 6);

	public abstract RoomFootprint Build(RandomNumberGenerator rng);

	public virtual string[] Validate()
	{
		var errors = new List<string>();
		if (MinimumSize.X < 1 || MinimumSize.Y < 1 || MaximumSize.X > 12 || MaximumSize.Y > 12
			|| MaximumSize.X < MinimumSize.X || MaximumSize.Y < MinimumSize.Y)
			errors.Add($"{GetType().Name}: use ordered sizes between 1 and 12 tiles.");
		return errors.ToArray();
	}

	/// <summary>Random size in range; length is never shorter than width, keeping the long axis on +Z.</summary>
	protected Vector2I RollSize(RandomNumberGenerator rng)
	{
		int width = rng.RandiRange(MinimumSize.X, MaximumSize.X);
		int length = rng.RandiRange(Mathf.Max(MinimumSize.Y, Mathf.Min(width, MaximumSize.Y)), MaximumSize.Y);
		return new(width, length);
	}

	protected static bool[,] Filled(Vector2I size)
	{
		var mask = new bool[size.X, size.Y];
		for (int x = 0; x < size.X; x++)
		for (int z = 0; z < size.Y; z++)
			mask[x, z] = true;
		return mask;
	}

	protected static void Carve(bool[,] mask, int x0, int z0, int width, int length)
	{
		for (int x = x0; x < x0 + width; x++)
		for (int z = z0; z < z0 + length; z++)
			if (x >= 0 && z >= 0 && x < mask.GetLength(0) && z < mask.GetLength(1)) mask[x, z] = false;
	}
}
