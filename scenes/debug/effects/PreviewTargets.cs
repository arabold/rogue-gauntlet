using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Shared visible-target ordering and cosmetic destinations for spell studies.</summary>
public static class PreviewTargets
{
	public static List<PreviewTargetStrategy.Segment> Select(World3D world, Vector3 origin, IEnumerable<Node3D> targets, AreaVisibility visibility, RandomNumberGenerator random, PreviewTargetStrategy strategy, int maximum)
	{
		var candidates = targets.Where(t => visibility.Contains(t.GlobalPosition) && WorldSight.IsClear(world, origin + Vector3.Up, t.GlobalPosition + Vector3.Up))
			.OrderBy(_ => random.Randf()).Select(t => t.GlobalPosition + Vector3.Up).ToList();
		return strategy.Select(world, origin + Vector3.Up * 1.3f, candidates, maximum);
	}
	public static bool TryCosmetic(World3D world, Vector3 origin, AreaVisibility visibility, RandomNumberGenerator random, out Vector3 destination)
	{
		float angle = random.RandfRange(0, Mathf.Tau);
		destination = origin + new Vector3(Mathf.Cos(angle) * 4, 1, Mathf.Sin(angle) * 4);
		return visibility.Contains(destination) && WorldSight.IsClear(world, origin + Vector3.Up, destination);
	}
}