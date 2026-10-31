using Godot;

/// <summary>A T: a full-width bar across one end and a centered stem along the room's length.</summary>
[Tool, GlobalClass]
public partial class TShape : RoomShape
{
	[Export(PropertyHint.Range, "1,6")] public int StemWidth { get; set; } = 2;
	[Export(PropertyHint.Range, "1,6")] public int BarDepth { get; set; } = 2;

	public override RoomFootprint Build(RandomNumberGenerator rng)
	{
		var size = RollSize(rng);
		var mask = Filled(size);
		int stem = Mathf.Min(StemWidth + (size.X - StemWidth) % 2, size.X);
		int side = (size.X - stem) / 2;
		bool barAtFront = rng.Randf() < .5f;
		int z0 = barAtFront ? BarDepth : 0;
		Carve(mask, 0, z0, side, size.Y - BarDepth);
		Carve(mask, size.X - side, z0, side, size.Y - BarDepth);
		return RoomFootprint.Trimmed(mask);
	}

	public override string[] Validate()
	{
		var errors = base.Validate();
		return MinimumSize.X < StemWidth + 2 || MinimumSize.Y <= BarDepth
			? [.. errors, "TShape: the minimum size must leave side wings beside the stem and a stem past the bar."]
			: errors;
	}
}
