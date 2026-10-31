using Godot;

/// <summary>A centerpiece in the middle of the room, aligned with its axis, with nearby fallbacks when blocked.</summary>
[Tool, GlobalClass]
public partial class CenterStep : FurnishingStep
{
	/// <summary>Also tries the centerpiece turned across the room.</summary>
	[Export] public bool AllowCrosswise { get; set; } = true;

	public CenterStep() => MaximumCount = 1;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		var middle = room.Interior.GetCenter();
		int count = 0;
		var plan = Next(room, rng, out var choice);
		Vector2[] offsets = [Vector2.Zero, new(0, -2), new(0, 2), new(-2, 0), new(2, 0), new(0, -4), new(0, 4)];
		foreach (var offset in offsets)
		{
			if (count >= MaximumCount || plan.IsEmpty) break;
			for (int turn = 0; turn < (AllowCrosswise ? 2 : 1); turn++)
			{
				var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
				var center = (new Transform3D(basis, Vector3.Zero) * plan.Bounds()).GetCenter();
				var at = new Transform3D(basis, new Vector3(middle.X + offset.X - center.X, 0, middle.Y + offset.Y - center.Z));
				if (!TryPlace(room, choice, plan, at)) continue;
				count++;
				plan = Next(room, rng, out choice);
				break;
			}
		}
		return count;
	}
}
