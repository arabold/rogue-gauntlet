using System.Linq;
using Godot;

/// <summary>
/// Places wall fixtures (typically torches or sconces) at corridor L-corners and T-junctions to landmark turns
/// and intersections. T-junctions prioritize the wall directly opposite the stem; L-corners use an outer corner wall.
/// </summary>
[Tool, GlobalClass]
public partial class CorridorCornerStep : CorridorStep
{
	[Export] public bool IncludeCorners { get; set; } = true;
	[Export] public bool IncludeTJunctions { get; set; } = true;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PerJunctionChance { get; set; } = .5f;

	public override void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng)
	{
		var junctions = corridors.Junctions
			.Where(junction => junction.IsTJunction ? IncludeTJunctions : IncludeCorners)
			.ToList();
		Shuffle(junctions, rng);
		int count = 0;

		foreach (var junction in junctions)
		{
			if (count >= MaximumCount) break;
			if (rng.Randf() >= PerJunctionChance) continue;

			if (junction.IsTJunction)
			{
				var prop = NextProp(corridors, rng);
				if (corridors.TryPlaceOnTileWall(prop, junction.Tile, junction.StemOppositeOutward, rng, minLightSpacing: Spacing))
				{
					count++;
				}
			}
			else
			{
				var outerWalls = new System.Collections.Generic.List<Vector2I>(2)
				{
					-junction.OpenDirections[0],
					-junction.OpenDirections[1],
				};
				Shuffle(outerWalls, rng);
				foreach (var outward in outerWalls)
				{
					var prop = NextProp(corridors, rng);
					if (!corridors.TryPlaceOnTileWall(prop, junction.Tile, outward, rng, minLightSpacing: Spacing)) continue;
					count++;
					break;
				}
			}
		}
	}
}