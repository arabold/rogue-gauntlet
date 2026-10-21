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

	public override PackedScene CreateEnemy(uint depth, RandomNumberGenerator rng) =>
		ContainsDepth(depth) ? Load(Pick(depth, int.MaxValue, rng)) : Fallback?.CreateEnemy(depth, rng);

	/// <summary>Spend a finite budget, filtering unaffordable entries before each weighted draw.</summary>
	public override Godot.Collections.Array<PackedScene> CreateEncounter(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreateEncounter(depth, rng) ?? [];
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

	private MobSpawnEntry Pick(uint depth, int budget, RandomNumberGenerator rng)
	{
		var eligible = Entries.Where(e => e != null && e.MinimumDepth <= depth && e.Threat > 0
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