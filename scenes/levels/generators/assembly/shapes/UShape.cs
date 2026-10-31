using Godot;

/// <summary>A U: a centered recess cut into one long side, leaving two wings joined by a back bar.</summary>
[Tool, GlobalClass]
public partial class UShape : RoomShape
{
	[Export(PropertyHint.Range, "1,6")] public int WingWidth { get; set; } = 2;
	[Export(PropertyHint.Range, "1,6")] public int BackDepth { get; set; } = 2;

	public override RoomFootprint Build(RandomNumberGenerator rng)
	{
		var size = RollSize(rng);
		var mask = Filled(size);
		int recessLength = size.Y - 2 * WingWidth;
		int recessDepth = size.X - BackDepth;
		bool left = rng.Randf() < .5f;
		Carve(mask, left ? 0 : size.X - recessDepth, WingWidth, recessDepth, recessLength);
		return RoomFootprint.Trimmed(mask);
	}

	public override string[] Validate()
	{
		var errors = base.Validate();
		return MinimumSize.Y < 2 * WingWidth + 1 || MinimumSize.X <= BackDepth
			? [.. errors, "UShape: the minimum size must leave a recess between both wings and behind the back bar."]
			: errors;
	}
}
