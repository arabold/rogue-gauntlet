using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Depth-scoped encounter definitions; runtime budget state stays local to each call. Each floor spends the budget
/// <see cref="ThreatProfile"/> gives its depth on packs, and each pack costs what its members' threat levels are worth
/// there as a group.
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

	/// <summary>What <paramref name="entry"/>'s pack spends of a floor budget at <paramref name="depth"/>.</summary>
	public float Cost(MobSpawnEntry entry, uint depth) => Cost(entry, depth, []);

	private float Cost(MobSpawnEntry entry, uint depth, Dictionary<string, PackedScene> scenes) =>
		ThreatProfile.PackCost(entry.Members.Select(path => DefinitionOf(Scene(path, scenes)).ThreatLevel).ToArray(), depth);

	/// <summary>Every realm floor has a plan, so its level runs the realm's encounter controller; floors outside defer to the fallback.</summary>
	public override EncounterPlan PlanEncounters(uint depth)
	{
		int floor = FloorIndex(depth);
		if (floor < 0) return Fallback?.PlanEncounters(depth);
		bool gatekeeper = floor == GatekeeperFloorIndex;
		return new EncounterPlan(this, floor == NestFloorIndex ? NestScene : null, gatekeeper ? GatekeeperScene : null,
			gatekeeper ? GatekeeperFit : null, gatekeeper ? $"depth:{depth}:gatekeeper" : null);
	}

	/// <summary>One monster on its own (a summon): a random member of a pack drawn by weight.</summary>
	public override PackedScene CreateEnemy(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreateEnemy(depth, rng);
		var scenes = RequireValidPool(depth);
		// Every pack is affordable on an unlimited budget, so only the weights decide.
		var entry = Pick(Eligible(depth).Select(e => (e, 0f)).ToArray(), float.PositiveInfinity, rng).Entry;
		return entry == null ? null : Scene(entry.Members[rng.RandiRange(0, entry.Members.Length - 1)], scenes);
	}

	/// <summary>Spend the floor's budget on packs, filtering unaffordable entries before each weighted draw.</summary>
	public override List<PackedScene[]> CreatePacks(uint depth, RandomNumberGenerator rng)
	{
		if (!ContainsDepth(depth)) return Fallback?.CreatePacks(depth, rng) ?? [];
		var scenes = RequireValidPool(depth);
		var costs = Costs(depth, scenes);
		var result = new List<PackedScene[]>();
		float remaining = FloorBudget(depth);
		int monsters = 0;
		while (remaining > 0 && monsters < MaximumMonsters)
		{
			var (entry, cost) = Pick(costs, remaining, rng);
			if (entry == null) break;
			result.Add(entry.Members.Select(path => Scene(path, scenes)).ToArray());
			monsters += entry.Members.Length;
			remaining -= cost;
		}
		return result;
	}

	/// <summary>A floor never spawns more ordinary monsters than this, however cheap they are.</summary>
	public const int MaximumMonsters = 32;

	/// <summary>Check authoring before the first random draw; invalid pools do not silently lose encounters.</summary>
	public string[] ValidatePool(uint depth) => ValidatePool(depth, []);

	/// <summary>Validates the pool, loading each member scene once into <paramref name="scenes"/> for the rest of the call.</summary>
	private string[] ValidatePool(uint depth, Dictionary<string, PackedScene> scenes)
	{
		var errors = new List<string>();
		if (!ContainsDepth(depth)) return errors.ToArray();
		if (ThreatProfile == null) return ["ThreatProfile: assign the profile that sets floor budgets and monster costs."];
		var ids = new Dictionary<string, string>();
		var checkedScenes = new HashSet<string>();
		foreach (var entry in Entries)
		{
			if (entry == null) { errors.Add("Encounter entry cannot be empty."); continue; }
			string name = string.Join(", ", entry.Members ?? []);
			if (entry.Weight <= 0 || entry.MinimumDepth < 1 || (entry.MaximumDepth != 0 && entry.MaximumDepth < entry.MinimumDepth))
				errors.Add($"{name}: invalid weight or depth bounds.");
			if (entry.Members == null || entry.Members.Length == 0) { errors.Add("Encounter entry: list its pack's members."); continue; }
			foreach (string path in entry.Members)
			{
				if (!checkedScenes.Add(path ?? "")) continue;
				if (string.IsNullOrWhiteSpace(path) || !ResourceLoader.Exists(path))
				{
					errors.Add($"Missing monster scene: {path}");
					continue;
				}
				errors.AddRange(MonsterSceneValidator.ValidateScene(Scene(path, scenes), out string id).Select(error => $"{path}: {error}"));
				if (ids.TryGetValue(id, out string previous) && previous != path) errors.Add($"Duplicate monster ID '{id}': {previous} and {path}");
				ids[id] = path;
			}
		}
		if (errors.Count == 0 && FloorBudget(depth) > 0 && !Costs(depth, scenes).Any(pair => pair.Cost <= FloorBudget(depth)))
			errors.Add($"Depth {depth} has no affordable eligible pack.");
		return errors.ToArray();
	}

	/// <summary>Validates the pool and returns its member scenes, loaded once for this call.</summary>
	private Dictionary<string, PackedScene> RequireValidPool(uint depth)
	{
		var scenes = new Dictionary<string, PackedScene>();
		var errors = ValidatePool(depth, scenes);
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
		return scenes;
	}

	/// <summary>Every entry that may spawn at <paramref name="depth"/>, in authored order.</summary>
	private IEnumerable<MobSpawnEntry> Eligible(uint depth) =>
		Entries.Where(e => e != null && e.EligibleAt(depth) && e.Weight > 0 && e.Members is { Length: > 0 });

	private (MobSpawnEntry Entry, float Cost)[] Costs(uint depth, Dictionary<string, PackedScene> scenes) =>
		Eligible(depth).Select(e => (e, Cost(e, depth, scenes))).ToArray();

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
	internal static MonsterDefinition DefinitionOf(PackedScene scene)
	{
		var state = scene.GetState();
		for (int i = 0; i < state.GetNodePropertyCount(0); i++)
			if (state.GetNodePropertyName(0, i) == EnemyBase.PropertyName.Definition)
				return state.GetNodePropertyValue(0, i).As<MonsterDefinition>();
		throw new InvalidOperationException($"{scene.ResourcePath}: the root saves no MonsterDefinition.");
	}

	/// <summary>
	/// A member scene, loaded on first use within one call and shared after it, so a floor loads each kind of monster once.
	/// </summary>
	private static PackedScene Scene(string path, Dictionary<string, PackedScene> scenes)
	{
		if (!scenes.TryGetValue(path, out var scene))
			scenes[path] = scene = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore)
				?? throw new InvalidOperationException($"Missing encounter scene: {path}");
		return scene;
	}
}
