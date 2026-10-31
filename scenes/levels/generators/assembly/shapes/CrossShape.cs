using Godot;

/// <summary>A plus: all four corners cut away around a centered cross of at least ArmWidth tiles.</summary>
[Tool, GlobalClass]
public partial class CrossShape : RoomShape
{
	[Export(PropertyHint.Range, "1,6")] public int ArmWidth { get; set; } = 2;

	public override RoomFootprint Build(RandomNumberGenerator rng)
	{
		var size = RollSize(rng);
		var mask = Filled(size);
		int armX = Mathf.Min(ArmWidth + (size.X - ArmWidth) % 2, size.X);
		int armZ = Mathf.Min(ArmWidth + (size.Y - ArmWidth) % 2, size.Y);
		int cutX = (size.X - armX) / 2, cutZ = (size.Y - armZ) / 2;
		Carve(mask, 0, 0, cutX, cutZ);
		Carve(mask, size.X - cutX, 0, cutX, cutZ);
		Carve(mask, 0, size.Y - cutZ, cutX, cutZ);
		Carve(mask, size.X - cutX, size.Y - cutZ, cutX, cutZ);
		return RoomFootprint.Trimmed(mask);
	}

	public override string[] Validate()
	{
		var errors = base.Validate();
		return MinimumSize.X < ArmWidth + 2 || MinimumSize.Y < ArmWidth + 2
			? [.. errors, "CrossShape: the minimum size must leave corners to cut on both axes."]
			: errors;
	}
}
