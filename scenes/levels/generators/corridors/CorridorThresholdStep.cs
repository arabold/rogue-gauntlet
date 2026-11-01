using System.Linq;
using Godot;

/// <summary>
/// Places wall fixtures (typically torches or sconces) on corridor tiles immediately outside room doorways.
/// Prefers the flanking corridor side walls and optionally pairs both sides of the threshold.
/// </summary>
[Tool, GlobalClass]
public partial class CorridorThresholdStep : CorridorStep
{
	[Export(PropertyHint.Range, "0,1,0.05")] public float PerThresholdChance { get; set; } = .55f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PairChance { get; set; } = .25f;

	public override void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng)
	{
		var thresholds = corridors.Thresholds.ToList();
		Shuffle(thresholds, rng);
		int count = 0;
		System.Span<Vector2I> sides = stackalloc Vector2I[2];

		foreach (var threshold in thresholds)
		{
			if (count >= MaximumCount) break;
			if (rng.Randf() >= PerThresholdChance) continue;

			bool pair = rng.Randf() < PairChance;
			bool leftFirst = rng.Randf() < .5f;
			sides[0] = leftFirst ? threshold.LeftOutward : threshold.RightOutward;
			sides[1] = leftFirst ? threshold.RightOutward : threshold.LeftOutward;

			int placedAtThreshold = 0;
			for (int i = 0; i < 2; i++)
			{
				if (count >= MaximumCount) break;
				var prop = NextProp(corridors, rng);
				float lightSpacing = placedAtThreshold > 0 && pair ? 0f : Spacing;
				if (!corridors.TryPlaceOnTileWall(prop, threshold.Tile, sides[i], rng, minLightSpacing: lightSpacing)) continue;
				count++;
				placedAtThreshold++;
				if (!pair) break;
			}

			// If the corridor turns immediately outside the doorway and neither side wall was available,
			// try the wall facing the doorway.
			if (placedAtThreshold == 0 && count < MaximumCount)
			{
				var prop = NextProp(corridors, rng);
				if (corridors.TryPlaceOnTileWall(prop, threshold.Tile, threshold.FromRoomDirection, rng, minLightSpacing: Spacing))
				{
					count++;
				}
			}
		}
	}
}