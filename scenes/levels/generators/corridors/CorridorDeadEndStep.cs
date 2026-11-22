using System.Linq;
using Godot;

/// <summary>
/// Places floor props (carts, ore piles, rubble) tucked against the end walls of dead-end mine shafts.
/// Skips dead ends already claimed by other props such as terminal rail stops.
/// </summary>
[Tool, GlobalClass]
public partial class CorridorDeadEndStep : CorridorStep
{
	[Export(PropertyHint.Range, "0,1,0.05")] public float PerDeadEndChance { get; set; } = 0.65f;

	public override void Decorate(CorridorBlueprint corridors, RandomNumberGenerator rng)
	{
		var deadEnds = corridors.DeadEnds.ToList();
		Shuffle(deadEnds, rng);
		int count = 0;

		foreach (var deadEnd in deadEnds)
		{
			if (count >= MaximumCount) break;
			if (rng.Randf() >= PerDeadEndChance) continue;

			var prop = NextProp(corridors, rng);
			if (corridors.TryPlaceInDeadEnd(prop, deadEnd, rng))
			{
				count++;
			}
		}
	}
}
