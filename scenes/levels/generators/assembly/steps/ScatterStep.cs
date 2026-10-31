using Godot;

/// <summary>Loose clutter: a few pieces against random walls or in the open, wherever they fit.</summary>
[Tool, GlobalClass]
public partial class ScatterStep : FurnishingStep
{
	/// <summary>Share of tries made against a wall; back-to-wall content always goes there.</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float WallChance { get; set; } = .7f;
	[Export(PropertyHint.Range, "1,64")] public int Attempts { get; set; } = 24;

	public override int Furnish(RoomBlueprint room, RandomNumberGenerator rng)
	{
		int count = 0;
		var plan = Next(room, rng, out var choice);
		var tiles = new System.Collections.Generic.List<Vector2I>(room.Footprint.Tiles);
		for (int attempt = 0; attempt < Attempts && count < MaximumCount && !plan.IsEmpty; attempt++)
		{
			bool wall = rng.Randf() < WallChance || plan.Pieces[0].Prop.BackToWall || plan.Pieces[0].Prop.Mount == PropMount.Wall;
			bool placed;
			if (wall && room.WallRuns.Count > 0)
			{
				var run = room.WallRuns[rng.RandiRange(0, room.WallRuns.Count - 1)];
				float width = room.WidthAlong(plan, run);
				float along = rng.RandfRange(.6f, Mathf.Max(.6f, run.LengthUnits - .6f - width));
				placed = TryPlaceAgainstWall(room, choice, plan, run, along);
			}
			else
			{
				var tile = tiles[rng.RandiRange(0, tiles.Count - 1)];
				var position = RoomFootprint.TileCenter(tile) + new Vector3(rng.RandfRange(-1, 1), 0, rng.RandfRange(-1, 1));
				placed = TryPlace(room, choice, plan, new Transform3D(new Basis(Vector3.Up, rng.RandiRange(0, 3) * Mathf.Pi / 2), position));
			}
			if (!placed) continue;
			count++;
			plan = Next(room, rng, out choice);
		}
		return count;
	}
}
