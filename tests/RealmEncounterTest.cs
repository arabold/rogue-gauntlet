namespace RogueGauntlet.Tests;

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
	public void BudgetsAreDeterministicScopedAndImmutable()
	{
		var factory = TestFactories.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		for (uint depth = 1; depth <= 3; depth++)
		{
			for (ulong seed = 1; seed <= 8; seed++)
			{
				using var a = GenerationRandom.Create(seed, GenerationRandom.Monsters);
				using var b = GenerationRandom.Create(seed, GenerationRandom.Monsters);
				var first = factory.CreateEncounter(depth, a).Select(s => s.ResourcePath).ToArray();
				var second = factory.CreateEncounter(depth, b).Select(s => s.ResourcePath).ToArray();
				AssertString(string.Join("|", first)).IsEqual(string.Join("|", second));
				// The floor spends its budget until no eligible monster still fits.
				float spent = first.Sum(path => factory.Cost(factory.Entries.Single(e => e.ScenePath == path), depth));
				float cheapest = factory.Entries.Where(e => e.EligibleAt(depth)).Min(e => factory.Cost(e, depth));
				AssertBool(first.Length > 0 && spent <= factory.FloorBudget(depth) + 1e-4f).IsTrue();
				AssertBool(first.Length == 24 || factory.FloorBudget(depth) - spent < cheapest).IsTrue();
			}
		}
		using var fallbackA = GenerationRandom.Create(42, GenerationRandom.Monsters);
		using var fallbackB = GenerationRandom.Create(42, GenerationRandom.Monsters);
		AssertString(string.Join("|", factory.CreateEncounter(4, fallbackA).Select(s => s.ResourcePath)))
			.IsEqual(string.Join("|", factory.Fallback.CreateEncounter(4, fallbackB).Select(s => s.ResourcePath)));
		AssertBool(factory.ContainsDepth(0)).IsFalse();
		AssertBool(factory.ContainsDepth(4)).IsFalse();
	}

	[TestCase]
	public async Task ThreeFloorsHaveFiniteNestsAndPersistentGatekeeperUnlock()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(2, 20);
		var level = runner.Scene().GetNode<Level>("Level");
		AssertBool(level.CanDescend).IsTrue();
		AssertBool(level.MapGenerator.EnemySpawnPoints.All(p => p.Scenes[0].ResourcePath.Contains("/skeleton/"))).IsTrue();

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
		AssertObject(nest.ReleaseMinion()).IsNotNull();
		AssertInt(nest.SummonsReleased).IsEqual(4);
		AssertObject(nest.ReleaseMinion()).IsNull();
		nest.TakeDamage(1000, Vector3.Forward, faction: DamageSourceFlags.Player);
		AssertBool(level.Run.IsCleared(nestId)).IsTrue();
		await runner.SimulateFrames(1, 20);
		level.Generate();
		AssertBool(level.FindChildren("*", "", true, false).OfType<DestructibleNest>()
			.Any(n => n.GetMeta("persistent_id").AsString() == nestId)).IsFalse();

		level.ConfigureGeneration(new RunState(12345), 3, null, null);
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
			int OrdinaryXp(MobSpawnEntry entry)
			{
				var actor = TestFactories.Load<PackedScene>(entry.ScenePath).Instantiate<EnemyBase>();
				try { return level.ThreatProfile.Xp(actor.Definition); }
				finally { actor.Free(); }
			}
			int ordinaryBest = realm.Entries.Max(OrdinaryXp);
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
		level.ConfigureGeneration(new RunState(12345), 3, null, null);
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
