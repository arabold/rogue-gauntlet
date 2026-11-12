using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Depth-scoped encounter definitions; runtime budget state stays local to each call. Each floor spends the budget
/// <see cref="ThreatProfile"/> gives its depth, and each monster costs what its threat level is worth there.
/// </summary>
[Tool, GlobalClass]
public partial class RealmEncounterFactory : MobFactory
{
	[Export] public string RealmName { get; set; } = "";
	[Export] public uint FirstDepth { get; set; } = 1;
	/// <summary>Floors in the realm, from <see cref="FirstDepth"/>; the last is the gatekeeper's.</summary>
	[Export(PropertyHint.Range, "1,10")] public uint FloorCount { get; set; } = 3;
	/// <summary>Floor budgets and monster costs.</summary>
	[Export] public ThreatProfile ThreatProfile { get; set; }
	[Export] public MobSpawnEntry[] Entries { get; set; } = [];
	[Export] public MobFactory Fallback { get; set; }
	[Export] public PackedScene NestScene { get; set; }
	[Export] public PackedScene GatekeeperScene { get; set; }
	[Export] public LargeActorFitProfile GatekeeperFit { get; set; }
	[Export] public string GatekeeperName { get; set; } = "Gatekeeper";

	public bool ContainsDepth(uint depth) => depth >= FirstDepth && depth - FirstDepth < FloorCount;
	public int FloorIndex(uint depth) => ContainsDepth(depth) ? (int)(depth - FirstDepth) : -1;
	/// <summary>Realm floor index whose first two encounter slots become destructible nests.</summary>
	public const int NestFloorIndex = 1;
	/// <summary>Realm floor index whose descent is sealed by the gatekeeper encounter.</summary>
	public const int GatekeeperFloorIndex = 2;

	/// <summary>Standard fights the ordinary monsters of the floor at <paramref name="depth"/> add up to.</summary>
	public float FloorBudget(uint depth) => ThreatProfile.FloorBudget(depth, FloorIndex(depth) == GatekeeperFloorIndex);

	/// <summary>What <paramref name="entry"/>'s monster spends of a floor budget at <paramref name="depth"/>.</summary>
	public float Cost(MobSpawnEntry entry, uint depth) => ThreatProfile.Cost(DefinitionOf(Load(entry)).ThreatLevel, depth);

	/// <summary>Every realm floor has a plan, so its level runs the realm's encounter controller; floors outside defer to the fallback.</summary>
	public override EncounterPlan PlanEncounters(uint depth)
	{
		int floor = FloorIndex(depth);
		if (floor < 0) return Fallback?.PlanEncounters(depth);
		bool gatekeeper = floor == GatekeeperFloorIndex;
		return new EncounterPlan(this, floor == NestFloorIndex ? NestScene : null, gatekeeper ? GatekeeperScene : null,
			gatekeeper ? GatekeeperFit : null, gatekeeper ? $"depth:{depth}:gatekeeper" : null);
	}

	public override PackedScene CreateEnemy(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreateEnemy(depth, rng);
		RequireValidPool(depth);
		return Load(Pick(Costs(depth), float.PositiveInfinity, rng).Entry);
	}

	/// <summary>Spend the floor's budget, filtering unaffordable entries before each weighted draw.</summary>
	public override Godot.Collections.Array<PackedScene> CreateEncounter(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreateEncounter(depth, rng) ?? [];
		RequireValidPool(depth);
		var costs = Costs(depth);
		var result = new Godot.Collections.Array<PackedScene>();
		float remaining = FloorBudget(depth);
		while (remaining > 0 && result.Count < 24)
		{
			var (entry, cost) = Pick(costs, remaining, rng);
			if (entry == null) break;
			var scene = Load(entry) ?? throw new InvalidOperationException($"Missing encounter scene: {entry.ScenePath}");
			result.Add(scene);
			remaining -= cost;
		}
		return result;
	}

	/// <summary>Check authoring before the first random draw; invalid pools do not silently lose encounters.</summary>
	public string[] ValidatePool(uint depth)
	{
		var errors = new List<string>();
		if (!ContainsDepth(depth)) return errors.ToArray();
		if (ThreatProfile == null) return ["ThreatProfile: assign the profile that sets floor budgets and monster costs."];
		var ids = new Dictionary<string, string>();
		foreach (var entry in Entries)
		{
			if (entry == null) { errors.Add("Encounter entry cannot be empty."); continue; }
			if (entry.Weight <= 0 || entry.MinimumDepth < 1 || (entry.MaximumDepth != 0 && entry.MaximumDepth < entry.MinimumDepth))
				errors.Add($"{entry.ScenePath}: invalid weight or depth bounds.");
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
		if (errors.Count == 0 && FloorBudget(depth) > 0 && !Costs(depth).Any(pair => pair.Cost <= FloorBudget(depth)))
			errors.Add($"Depth {depth} has no affordable eligible monster.");
		return errors.ToArray();
	}

	private void RequireValidPool(uint depth)
	{
		var errors = ValidatePool(depth);
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
	}

	/// <summary>The cost of every entry that may spawn at <paramref name="depth"/>, in authored order.</summary>
	private (MobSpawnEntry Entry, float Cost)[] Costs(uint depth) => Entries
		.Where(e => e != null && e.EligibleAt(depth) && e.Weight > 0 && !string.IsNullOrWhiteSpace(e.ScenePath))
		.Select(e => (e, Cost(e, depth))).ToArray();

	private static (MobSpawnEntry Entry, float Cost) Pick((MobSpawnEntry Entry, float Cost)[] costs, float budget, RandomNumberGenerator rng)
	{
		var affordable = costs.Where(pair => pair.Cost <= budget).ToArray();
		int total = affordable.Sum(pair => pair.Entry.Weight);
		if (total == 0) return default;
		int roll = rng.RandiRange(1, total);
		foreach (var pair in affordable) { roll -= pair.Entry.Weight; if (roll <= 0) return pair; }
		return default;
	}

	/// <summary>The definition a monster scene's root is saved with, read without instantiating the scene.</summary>
	private static MonsterDefinition DefinitionOf(PackedScene scene)
	{
		var state = scene.GetState();
		for (int i = 0; i < state.GetNodePropertyCount(0); i++)
			if (state.GetNodePropertyName(0, i) == EnemyBase.PropertyName.Definition)
				return state.GetNodePropertyValue(0, i).As<MonsterDefinition>();
		throw new InvalidOperationException($"{scene.ResourcePath}: the root saves no MonsterDefinition.");
	}

	private static PackedScene Load(MobSpawnEntry entry) => entry == null ? null :
		ResourceLoader.Load<PackedScene>(entry.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
}
