using System.Collections.Generic;
using Godot;
using Godot.Collections;

/// <summary>
/// Depth-aware enemy factory: each entry declares a depth window and a relative weight,
/// so shallow floors spawn fodder while tougher enemies fade in further down.
/// </summary>
[Tool]
[GlobalClass]
public partial class DungeonMobFactory : MobFactory
{
	/// <summary>Weighted, depth-gated enemy pool.</summary>
	[Export] public Array<DungeonMobEntry> Entries { get; set; } = [];

	public override PackedScene CreateEnemy(uint dungeonDepth)
	{
		string scenePath = PickScenePath(dungeonDepth);
		if (string.IsNullOrEmpty(scenePath))
		{
			GD.PrintErr($"Dungeon mob factory has no eligible enemy for depth {dungeonDepth}.");
			return null;
		}

		// Ignore the global cache so enemies from old floors are not held after scene reloads.
		PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (scene == null)
		{
			GD.PrintErr($"Could not load dungeon enemy scene: {scenePath}");
		}

		return scene;
	}

	/// <summary>
	/// Weighted pick among the entries whose depth window contains the given depth.
	/// Uses the global RNG (like the level generator) so spawns stay reproducible per map seed.
	/// </summary>
	private string PickScenePath(uint dungeonDepth)
	{
		var eligible = new List<DungeonMobEntry>();
		float totalWeight = 0f;
		foreach (DungeonMobEntry entry in Entries)
		{
			if (entry != null && entry.Weight > 0f && entry.IsEligibleAt(dungeonDepth))
			{
				eligible.Add(entry);
				totalWeight += entry.Weight;
			}
		}

		if (eligible.Count == 0)
		{
			return null;
		}

		float roll = GD.Randf() * totalWeight;
		foreach (DungeonMobEntry entry in eligible)
		{
			roll -= entry.Weight;
			if (roll <= 0f)
			{
				return entry.ScenePath;
			}
		}

		// Float rounding can leave a sliver of roll; fall back to the last eligible entry.
		return eligible[^1].ScenePath;
	}
}
