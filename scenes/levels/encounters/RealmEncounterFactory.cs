using Godot;
using System;
using System.Linq;

/// <summary>Depth-scoped encounter definitions; runtime budget state stays local to each call.</summary>
[Tool, GlobalClass]
public partial class RealmEncounterFactory : MobFactory
{
	[Export] public string RealmName { get; set; } = "";
	[Export] public uint FirstDepth { get; set; } = 1;
	[Export] public int[] FloorBudgets { get; set; } = [12, 20, 16];
	[Export] public MobSpawnEntry[] Entries { get; set; } = [];
	[Export] public MobFactory Fallback { get; set; }
	[Export] public PackedScene NestScene { get; set; }
	[Export] public PackedScene GatekeeperScene { get; set; }
	[Export] public LargeActorFitProfile GatekeeperFit { get; set; }
	[Export] public string GatekeeperName { get; set; } = "Gatekeeper";

	public bool ContainsDepth(uint depth) => depth >= FirstDepth && depth - FirstDepth < FloorBudgets.Length;
	public int FloorIndex(uint depth) => ContainsDepth(depth) ? (int)(depth - FirstDepth) : -1;

	public override PackedScene CreateEnemy(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreateEnemy(depth, rng);
		RequireValidPool(depth);
		return Load(Pick(depth, int.MaxValue, rng));
	}

	/// <summary>Spend a finite budget, filtering unaffordable entries before each weighted draw.</summary>
	public override Godot.Collections.Array<PackedScene> CreateEncounter(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreateEncounter(depth, rng) ?? [];
		RequireValidPool(depth);
		var result = new Godot.Collections.Array<PackedScene>();
		int remaining = FloorBudgets[FloorIndex(depth)];
		while (remaining > 0 && result.Count < 24)
		{
			var entry = Pick(depth, remaining, rng);
			if (entry == null) break;
			var scene = Load(entry) ?? throw new InvalidOperationException($"Missing encounter scene: {entry.ScenePath}");
			result.Add(scene);
			remaining -= entry.Threat;
		}
		return result;
	}

	/// <summary>Check authoring before the first random draw; invalid pools do not silently lose encounters.</summary>
	public string[] ValidatePool(uint depth)
	{
		var errors = new System.Collections.Generic.List<string>();
		if (!ContainsDepth(depth)) return errors.ToArray();
		int budget = FloorBudgets[FloorIndex(depth)];
		if (budget < 0) errors.Add("Floor budget cannot be negative.");
		var ids = new System.Collections.Generic.Dictionary<string, string>();
		foreach (var entry in Entries)
		{
			if (entry == null) { errors.Add("Encounter entry cannot be empty."); continue; }
			if (entry.Threat <= 0 || entry.Weight <= 0 || entry.MinimumDepth < 1
				|| (entry.MaximumDepth != 0 && entry.MaximumDepth < entry.MinimumDepth))
				errors.Add($"{entry.ScenePath}: invalid threat, weight or depth bounds.");
			if (string.IsNullOrWhiteSpace(entry.ScenePath) || !ResourceLoader.Exists(entry.ScenePath))
			{
				errors.Add($"Missing monster scene: {entry.ScenePath}");
				continue;
			}
			var scene = Load(entry);
			errors.AddRange(MonsterSceneValidator.ValidateScene(scene, out string id).Select(error => $"{entry.ScenePath}: {error}"));
			if (ids.TryGetValue(id, out string previous) && previous != entry.ScenePath) errors.Add($"Duplicate monster ID '{id}': {previous} and {entry.ScenePath}");
			ids[id] = entry.ScenePath;
		}
		if (budget > 0 && !Entries.Any(entry => entry != null && entry.EligibleAt(depth) && entry.Weight > 0 && entry.Threat > 0 && entry.Threat <= budget))
			errors.Add($"Depth {depth} has no affordable eligible monster.");
		return errors.ToArray();
	}

	private void RequireValidPool(uint depth)
	{
		var errors = ValidatePool(depth);
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
	}

	private MobSpawnEntry Pick(uint depth, int budget, RandomNumberGenerator rng)
	{
		var eligible = Entries.Where(e => e != null && e.EligibleAt(depth) && e.Threat > 0
			&& e.Threat <= budget && e.Weight > 0 && !string.IsNullOrWhiteSpace(e.ScenePath)).ToArray();
		int total = eligible.Sum(e => e.Weight);
		if (total == 0) return null;
		int roll = rng.RandiRange(1, total);
		foreach (var entry in eligible) { roll -= entry.Weight; if (roll <= 0) return entry; }
		return null;
	}

	private static PackedScene Load(MobSpawnEntry entry) => entry == null ? null :
		ResourceLoader.Load<PackedScene>(entry.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
}