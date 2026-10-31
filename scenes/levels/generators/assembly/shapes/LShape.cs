using Godot;

/// <summary>An L: one corner of the bounding rectangle is cut away, leaving two arms at least ArmWidth tiles wide.</summary>
[Tool, GlobalClass]
public partial class LShape : RoomShape
{
	[Export(PropertyHint.Range, "1,6")] public int ArmWidth { get; set; } = 2;

	public override RoomFootprint Build(RandomNumberGenerator rng)
	{
		var size = RollSize(rng);
		var mask = Filled(size);
		int cutX = rng.RandiRange(1, Mathf.Max(1, size.X - ArmWidth));
		int cutZ = rng.RandiRange(1, Mathf.Max(1, size.Y - ArmWidth));
		int corner = rng.RandiRange(0, 3);
		Carve(mask, (corner & 1) == 0 ? 0 : size.X - cutX, (corner & 2) == 0 ? 0 : size.Y - cutZ, cutX, cutZ);
		return RoomFootprint.Trimmed(mask);
	}

	public override string[] Validate()
	{
		var errors = base.Validate();
		return MinimumSize.X <= ArmWidth || MinimumSize.Y <= ArmWidth
			? [.. errors, "LShape: the minimum size must exceed the arm width on both axes."]
			: errors;
	}
}
