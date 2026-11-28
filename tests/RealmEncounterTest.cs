namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Real authored content, finite spawning, progression and regenerated cleared-state checks.</summary>
[TestSuite, RequireGodotRuntime]
public class RealmEncounterTest
{
	[TestCase]
	public void BudgetsAreDeterministicScopedAndKeepMonsterLimits()
	{
		var factory = TestFactories.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		var threat = TestFactories.Load<ThreatProfile>(TestFactories.ThreatProfilePath);
		// Every realm down the fallback chain spends its floors' budgets the same way.
		bool limitReached = false;
		var definitions = new Dictionary<string, MonsterDefinition>();
		for (var realm = factory; realm != null; realm = realm.Fallback as RealmEncounterFactory)
			for (uint depth = realm.FirstDepth; realm.ContainsDepth(depth); depth++)
				limitReached |= AssertBudgetSpent(realm, depth, threat, definitions);
		// Some limited monster spawns on a floor, up to its limit: limits thin a monster out without removing it.
		AssertBool(limitReached).IsTrue();
		using var fallbackA = GenerationRandom.Create(42, GenerationRandom.Monsters);
		using var fallbackB = GenerationRandom.Create(42, GenerationRandom.Monsters);
		AssertString(Fingerprint(Members(factory.CreatePacks(TestFactories.GarrisonGatekeeperDepth + 1, threat, fallbackA))))
			.IsEqual(Fingerprint(Members(factory.Fallback.CreatePacks(TestFactories.GarrisonGatekeeperDepth + 1, threat, fallbackB))));
		AssertBool(factory.ContainsDepth(0)).IsFalse();
		AssertBool(factory.ContainsDepth(TestFactories.GarrisonGatekeeperDepth)).IsTrue();
		AssertBool(factory.ContainsDepth(TestFactories.GarrisonGatekeeperDepth + 1)).IsFalse();
	}

	/// <summary>
	/// Checks eight seeded floors at <paramref name="depth"/>: each spends its budget on whole packs and never fields more
	/// of a monster than its <see cref="MonsterDefinition.MaximumPerFloor"/>. Returns whether a limited monster spawned.
	/// <paramref name="definitions"/> caches each member scene's definition across depths, so each scene loads once.
	/// </summary>
	private static bool AssertBudgetSpent(RealmEncounterFactory factory, uint depth, ThreatProfile threat,
		Dictionary<string, MonsterDefinition> definitions)
	{
		var costs = factory.Entries.Where(e => e.EligibleAt(depth)).ToDictionary(e => e, e => RealmEncounterFactory.Cost(e, depth, threat));
		foreach (string path in costs.Keys.SelectMany(e => e.Members).Distinct().Where(path => !definitions.ContainsKey(path)))
			definitions[path] = RealmEncounterFactory.DefinitionOf(TestFactories.Load<PackedScene>(path));
		bool limited = false;
		for (ulong seed = 1; seed <= 8; seed++)
		{
			using var a = GenerationRandom.Create(seed, GenerationRandom.Monsters);
			using var b = GenerationRandom.Create(seed, GenerationRandom.Monsters);
			var first = Members(factory.CreatePacks(depth, threat, a));
			var second = Members(factory.CreatePacks(depth, threat, b));
			AssertString(Fingerprint(first)).IsEqual(Fingerprint(second));
			var spawned = first.SelectMany(pack => pack).GroupBy(path => definitions[path]).ToDictionary(kind => kind.Key, kind => kind.Count());
			foreach (var (definition, count) in spawned.Where(pair => pair.Key.MaximumPerFloor > 0))
			{
				AssertInt(count).OverrideFailureMessage($"{definition.DisplayName} at depth {depth}, seed {seed}")
					.IsLessEqual(definition.MaximumPerFloor);
				limited = true;
			}
			// The floor spends its budget on whole packs until no eligible pack still fits within the monsters' limits.
			float spent = first.Sum(pack => costs[costs.Keys.Single(e => e.Members.SequenceEqual(pack))]);
			var byId = spawned.ToDictionary(pair => pair.Key.Id, pair => pair.Value);
			float cheapest = costs.Where(pair => RealmEncounterFactory.WithinLimits(pair.Key.Members.Select(path => definitions[path]), byId))
				.Select(pair => pair.Value).DefaultIfEmpty(float.PositiveInfinity).Min();
			AssertBool(first.Length > 0 && spent <= factory.FloorBudget(depth, threat) + 1e-4f).IsTrue();
			AssertBool(first.Sum(pack => pack.Length) >= RealmEncounterFactory.MaximumMonsters
				|| factory.FloorBudget(depth, threat) - spent < cheapest).IsTrue();
		}
		return limited;
	}

	/// <summary>
	/// The realms after the Garrison, down its fallback chain, with the monsters each fields, its nest and its gatekeeper
	/// (null while the realm has none yet).
	/// </summary>
	private static readonly (string Name, string[] Cast, string Nest, string Gatekeeper)[] LaterRealms =
	[
		("The Prison", ["res://scenes/enemies/orc/orc_warrior.tscn", "res://scenes/enemies/orc/orc_spearman.tscn",
			"res://scenes/enemies/orc/orc_shaman.tscn", "res://scenes/enemies/orc/orc_captain.tscn", "res://scenes/enemies/bat/giant_bat.tscn",
			"res://scenes/enemies/nymph/nymph.tscn"],
			"res://scenes/levels/encounters/bat_roost.tscn", "res://scenes/enemies/minotaur/minotaur.tscn"),
		("The Sewer", ["res://scenes/enemies/rat/sewer_rat.tscn", "res://scenes/enemies/slime/acid_blob.tscn", "res://scenes/enemies/snake/snake.tscn",
			"res://scenes/enemies/slime/spitting_slime.tscn"],
			"res://scenes/levels/encounters/slime_vat.tscn", "res://scenes/enemies/gelatinous_cube/gelatinous_cube.tscn"),
		("The Mines", ["res://scenes/enemies/golem/stone_golem.tscn", "res://scenes/enemies/bat/giant_bat.tscn", "res://scenes/enemies/snake/snake.tscn"],
			"res://scenes/levels/encounters/bat_roost.tscn", null),
	];

	[TestCase]
	public void LaterRealmsFieldTheirOwnCastsAndEndOnTheirGatekeeper()
	{
		var realm = TestFactories.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		var threat = TestFactories.Load<ThreatProfile>(TestFactories.ThreatProfilePath);
		foreach (var (name, cast, _, gatekeeper) in LaterRealms)
		{
			uint previousLast = realm.FirstDepth + realm.FloorCount - 1;
			realm = (RealmEncounterFactory)realm.Fallback;
			AssertString(realm.RealmName).IsEqual(name);
			AssertInt((int)realm.FirstDepth).IsEqual((int)previousLast + 1);
			uint last = realm.FirstDepth + realm.FloorCount - 1;
			for (uint depth = realm.FirstDepth; depth <= last; depth++)
			{
				using var rng = GenerationRandom.Create(depth, GenerationRandom.Monsters);
				AssertBool(Members(realm.CreatePacks(depth, threat, rng)).SelectMany(pack => pack).All(cast.Contains))
					.OverrideFailureMessage($"{name} depth {depth}").IsTrue();
			}
			// Its last floor holds its gatekeeper and keeps the gatekeeper's share of the budget; a realm without one yet
			// ends on an ordinary floor that spends a full floor's budget.
			var plan = realm.PlanEncounters(last);
			AssertString(plan.Gatekeeper?.ResourcePath).OverrideFailureMessage(name).IsEqual(gatekeeper);
			AssertBool(plan.GatekeeperId != null).OverrideFailureMessage(name).IsEqual(gatekeeper != null);
			AssertFloat(realm.FloorBudget(last, threat)).IsEqual(threat.FloorBudget(last, gatekeeper != null));
		}
	}

	[TestCase]
	public async Task GeneratedLaterRealmFloorsSpawnTheirCastNestsAndGatekeeper()
	{
		// Each realm's nest floor and last floor: their nests release the realm's own monsters, and the last floor seals
		// its descent behind the realm's gatekeeper, or stays open while the realm has none yet.
		(uint Depth, int Realm, int Nests)[] floors = [(7, 0, 2), (10, 0, 0), (12, 1, 2), (15, 1, 0), (17, 2, 2)];
		var (runner, level) = await TestFactories.GenerateLevel(23, floors[0].Depth);
		using var cleanup = runner;
		foreach (var (depth, realm, nests) in floors)
		{
			var (_, cast, nest, gatekeeper) = LaterRealms[realm];
			bool sealedByGatekeeper = gatekeeper != null && depth == floors.Where(f => f.Realm == realm).Max(f => f.Depth);
			if (depth != floors[0].Depth)
			{
				level.ConfigureGeneration(new RunState(23), depth, null, null);
				level.Generate();
			}
			// From the Prison on, a placed chest may also be a mimic.
			var spawns = level.MapGenerator.EnemySpawnPoints;
			AssertBool(spawns.Count > 0 && spawns.Where(spawn => !level.Floor.IsNest(spawn)).All(spawn => spawn.Scenes.All(scene => cast.Contains(scene.ResourcePath)
				|| scene.ResourcePath == "res://scenes/enemies/mimic/chest_mimic.tscn" && spawn.PersistentId.Contains(":chest:")
				|| sealedByGatekeeper && scene.ResourcePath == gatekeeper && spawn.PersistentId == level.Encounters.GatekeeperId)))
				.OverrideFailureMessage($"depth={depth}").IsTrue();
			AssertInt(spawns.Count(spawn => level.Floor.IsNest(spawn) && spawn.Scenes[0].ResourcePath == nest)).OverrideFailureMessage($"depth={depth}").IsEqual(nests);
			await runner.SimulateFrames(1, 20);
			var placed = level.FindChildren("*", "", true, false).OfType<DestructibleNest>().ToList();
			AssertBool(placed.Count == nests && placed.All(n => cast.Contains(n.MinionScene.ResourcePath))).OverrideFailureMessage($"depth={depth}").IsTrue();
			AssertBool(level.CanDescend).OverrideFailureMessage($"depth={depth}").IsEqual(!sealedByGatekeeper);
			if (sealedByGatekeeper)
				AssertInt(spawns.Count(spawn => spawn.PersistentId == level.Encounters.GatekeeperId && spawn.Scenes[0].ResourcePath == gatekeeper))
					.OverrideFailureMessage($"depth={depth}").IsEqual(1);
		}
	}

	private static string[][] Members(System.Collections.Generic.List<PackedScene[]> packs) =>
		packs.Select(pack => pack.Select(scene => scene.ResourcePath).ToArray()).ToArray();

	private static string Fingerprint(string[][] packs) => string.Join("|", packs.Select(pack => string.Join(",", pack)));

	[TestCase]
	public async Task RealmFloorsHaveFiniteNestsAndPersistentGatekeeperUnlock()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(2, 20);
		var level = runner.Scene().GetNode<Level>("Level");
		AssertBool(level.CanDescend).IsTrue();
		// Depth 1 draws from the realm's own packs, not the fallback.
		var realmMembers = ((RealmEncounterFactory)level.MapGenerator.MobFactory).Entries.SelectMany(entry => entry.Members).ToHashSet();
		AssertBool(level.MapGenerator.EnemySpawnPoints.All(p => realmMembers.Contains(p.Scenes[0].ResourcePath))).IsTrue();

		level.ConfigureGeneration(new RunState(12345), 2, null, null);
		level.Generate();
		await runner.SimulateFrames(1, 20);
		var nest = level.FindChildren("*", "", true, false).OfType<DestructibleNest>().First();
		nest.SetProcess(false);
		string nestId = nest.GetMeta("persistent_id").AsString();
		var first = nest.ReleaseMinion();
		AssertObject(first).IsNotNull();
		AssertObject(nest.ReleaseMinion()).IsNotNull();
		AssertObject(nest.ReleaseMinion()).IsNull();
		AssertInt(nest.LivingMinions).IsEqual(2);
		first.HealthComponent.TakeDamage(1000);
		AssertObject(nest.ReleaseMinion()).IsNotNull();
		foreach (var enemy in level.FindChildren("*", "", true, false).OfType<EnemyBase>()
			.Where(e => e.GetMeta("persistent_id", "").AsString().StartsWith(nestId + ":summon:") && !e.IsDead))
			enemy.HealthComponent.TakeDamage(1000);
		var survivor = nest.ReleaseMinion();
		AssertObject(survivor).IsNotNull();
		AssertInt(nest.SummonsReleased).IsEqual(4);
		AssertObject(nest.ReleaseMinion()).IsNull();
		nest.TakeDamage(1000, Vector3.Forward, faction: DamageSourceFlags.Player);
		AssertBool(level.Run.IsCleared(nestId)).IsTrue();
		await runner.SimulateFrames(1, 20);
		AssertBool(GodotObject.IsInstanceValid(survivor) && !survivor.IsQueuedForDeletion() && !survivor.IsDead).IsTrue();
		level.Generate();
		AssertBool(level.FindChildren("*", "", true, false).OfType<DestructibleNest>()
			.Any(n => n.GetMeta("persistent_id").AsString() == nestId)).IsFalse();

		level.ConfigureGeneration(new RunState(12345), TestFactories.GarrisonGatekeeperDepth, null, null);
		level.Generate();
		AssertBool(level.CanDescend).IsFalse();
		var boss = level.FindChildren("*", "", true, false).OfType<EnemyBase>()
			.Single(e => e.GetMeta("persistent_id", "").AsString() == level.Encounters.GatekeeperId);
		AssertFloat(boss.HealthComponent.CurrentHealth).IsEqual(boss.Definition.MaxHealth);
		AssertString(boss.SceneFilePath).IsEqual("res://scenes/enemies/forsaken_warden/forsaken_warden.tscn");
		var tree = level.GetTree();
		Node previousScene = tree.CurrentScene;
		tree.CurrentScene = runner.Scene();
		try
		{
			AssertBool(GameSession.Instance.ChangeDungeonDepth(GameSession.LevelTravelDirection.Down)).IsFalse();
			var player = level.GetTree().GetNodesInGroup("player").OfType<Player>().Single();
			int xp = player.Stats.Xp;
			boss.HealthComponent.TakeDamage(1000);
			// Its threat level and the gatekeeper bonus pay more than any ordinary monster of the realm.
			var realm = (RealmEncounterFactory)level.MapGenerator.MobFactory;
			int OrdinaryXp(string path) => level.ThreatProfile.Xp(RealmEncounterFactory.DefinitionOf(TestFactories.Load<PackedScene>(path)));
			int ordinaryBest = realm.Entries.SelectMany(entry => entry.Members).Distinct().Max(OrdinaryXp);
			AssertInt(player.Stats.Xp - xp).IsGreater(ordinaryBest);
			await Task.Delay(1100); // allow the independent stair-arrival cooldown to expire
			AssertBool(GameSession.Instance.CanUseTransition(GameSession.LevelTravelDirection.Down)).IsTrue();
		}
		finally { tree.CurrentScene = previousScene; }
		AssertBool(level.CanDescend).IsTrue();
		AssertBool(level.Run.IsCleared(level.Encounters.GatekeeperId)).IsTrue();
		level.Generate();
		AssertBool(level.CanDescend).IsTrue();
		AssertBool(level.FindChildren("*", "", true, false).OfType<EnemyBase>()
			.Any(e => e.GetMeta("persistent_id", "").AsString() == level.Encounters.GatekeeperId)).IsFalse();
		AssertInt(level.MapGenerator.EnemySpawnPoints.Count(p => p.PersistentId == level.Encounters.GatekeeperId)).IsEqual(1);
	}

	[TestCase]
	public async Task GatekeeperFloorWithoutAGatekeeperSceneKeepsItsDescentSealed()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2, 20);
		var level = (Level)runner.Scene();
		var realm = (RealmEncounterFactory)level.MapGenerator.MobFactory.Duplicate();
		realm.GatekeeperScene = null;
		level.MapGenerator.MobFactory = realm;
		level.ConfigureGeneration(new RunState(12345), TestFactories.GarrisonGatekeeperDepth, null, null);
		level.Generate();
		AssertObject(level.Floor).IsNotNull();
		AssertObject(level.Floor.GatekeeperSpawn).IsNull();
		AssertBool(level.CanDescend).IsFalse();
		await runner.SimulateFrames(1, 20);
	}

	[TestCase]
	public async Task CrossbowSkeletonShootsABoltAtItsTarget()
	{
		using var runner = TestFactories.LoadScene("res://scenes/enemies/skeleton/skeleton_crossbow.tscn");
		await runner.SimulateFrames(2, 20);
		var actor = runner.Scene();
		var animation = actor.GetNode<CombatAnimationComponent>("CombatAnimationComponent");
		AssertBool(animation.TryResolve("ranged", out _)).IsTrue();
		var behavior = actor.GetNode<EnemyBehaviorComponent>("EnemyBehaviorComponent").Profile;
		var action = (MonsterAttackAction)behavior.Combat.PrimaryOffense;
		var controller = actor.GetNode<AttackController>("AttackController");
		// The shot locks onto a target before release, so place a player-group target straight ahead within range.
		var shooter = (Node3D)actor;
		Vector3 forward = (-shooter.GlobalTransform.Basis.Z with { Y = 0 }).Normalized();
		var target = new Node3D { Name = "AimTarget" };
		target.AddToGroup("player");
		shooter.GetParent().AddChild(target);
		target.GlobalPosition = shooter.GlobalPosition + forward * 6;
		AssertBool(controller.TryPrepareAttack(action.Attack, action.Style, action.Duration, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, new AttackStats(3, 6, 0), 4)).IsTrue();
		Node projectileParent = actor.GetTree().CurrentScene ?? actor.GetParent();
		var before = projectileParent.GetChildren().OfType<Projectile>().ToHashSet();
		// Cross the aim lock and the prepared release time in one step, without relying on frame timing.
		controller._PhysicsProcess(attack.Definition.HitWindowStart + .05);
		AssertBool(controller.HasCommittedFacing).IsTrue();
		var arrow = projectileParent.GetChildren().OfType<Projectile>().Single(p => !before.Contains(p));
		AssertFloat(arrow.Hit.MinDamage).IsEqual(3);
		AssertFloat((arrow.Direction with { Y = 0 }).Normalized().Dot(forward)).IsGreater(0.95f);
		ScenePool.Despawn(arrow);
		controller.CancelAttack();
		target.QueueFree();
	}
}
