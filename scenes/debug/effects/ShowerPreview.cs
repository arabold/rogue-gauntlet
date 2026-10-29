using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Builds visual-only shower placements, cycling through targets before repeating them.</summary>
public static class ShowerPreview
{
	public readonly record struct Placement(Vector3 Position, bool Cosmetic);

	public static List<Placement> Build(World3D world, Vector3 origin, IEnumerable<Node3D> targets, int strikes, int extras, ulong seed)
	{
		var candidates = targets.Where(t => GodotObject.IsInstanceValid(t) && t.IsInsideTree()
			&& WorldSight.IsClear(world, origin + Vector3.Up, t.GlobalPosition + Vector3.Up)).ToList();
		var available = new List<Node3D>();
		var random = new RandomNumberGenerator { Seed = seed };
		var visualRandom = new RandomNumberGenerator { Seed = seed ^ 0xB33F17UL };
		var placements = new List<Placement>();
		if (candidates.Count > 0)
			for (int i = 0; i < strikes; i++)
			{
				if (available.Count == 0) available.AddRange(candidates);
				int index = random.RandiRange(0, available.Count - 1);
				placements.Add(new Placement(available[index].GlobalPosition, false));
				available.RemoveAt(index);
			}
		int cosmeticCount = extras + (candidates.Count == 0 ? strikes : 0);
		for (int i = 0; i < cosmeticCount; i++)
			for (int attempt = 0; attempt < 16; attempt++)
			{
				float angle = visualRandom.RandfRange(0, Mathf.Tau), radius = visualRandom.RandfRange(3, 6);
				Vector3 position = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
				if (!WorldSight.IsClear(world, origin + Vector3.Up, position + Vector3.Up)) continue;
				placements.Add(new Placement(position, true)); break;
			}
		return placements;
	}
}
