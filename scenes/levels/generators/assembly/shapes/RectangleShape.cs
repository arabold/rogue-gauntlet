using Godot;

/// <summary>A plain rectangle; odd widths center an aisle on doorways at both ends.</summary>
[Tool, GlobalClass]
public partial class RectangleShape : RoomShape
{
	[Export] public bool OddWidth { get; set; }

	public override RoomFootprint Build(RandomNumberGenerator rng)
	{
		var size = RollSize(rng);
		if (OddWidth && size.X % 2 == 0) size.X += size.X < MaximumSize.X ? 1 : -1;
		return new RoomFootprint(Filled(size));
	}

	public override string[] Validate()
	{
		var errors = base.Validate();
		return OddWidth && MinimumSize.X == MaximumSize.X && MinimumSize.X % 2 == 0
			? [.. errors, "RectangleShape: an odd width needs an odd value in the width range."]
			: errors;
	}
}
