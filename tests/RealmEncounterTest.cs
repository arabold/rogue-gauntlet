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
		var factory = GD.Load<RealmEncounterFactory>("res://scenes/levels/encounters/sunken_garrison.tres");
		for (uint depth = 1; depth <= 3; depth++)
		{
			for (ulong seed = 1; seed <= 8; seed++)
			{
				using var a = GenerationRandom.Create(seed, GenerationRandom.Monsters);
				using var b = GenerationRandom.Create(seed, GenerationRandom.Monsters);
				var first = factory.CreateEncounter(depth, a).Select(s => s.ResourcePath).ToArray();
				var second = factory.CreateEncounter(depth, b).Select(s => s.ResourcePath).ToArray();
				AssertString(string.Join("|", first)).IsEqual(string.Join("|", second));
				int spent = first.Sum(path => factory.Entries.Single(e => e.ScenePath == path).Threat);
				AssertInt(spent).IsEqual(factory.FloorBudgets[(int)depth - 1]);
			}
		}
		using var fallbackA = GenerationRandom.Create(42, GenerationRandom.Monsters);
		using var fallbackB = GenerationRandom.Create(42, GenerationRandom.Monsters);
		AssertString(string.Join("|", factory.CreateEncounter(4, fallbackA).Select(s => s.ResourcePath)))
			.IsEqual(string.Join("|", factory.Fallback.CreateEncounter(4, fallbackB).Select(s => s.ResourcePath)));
		AssertBool(factory.ContainsDepth(0)).IsFalse();
		AssertBool(factory.ContainsDepth(4)).IsFalse();
		AssertString(string.Join(",", factory.FloorBudgets)).IsEqual("12,20,16");
	}

	[TestCase]
	public async Task ThreeFloorsHaveFiniteNestsAndPersistentGatekeeperUnlock()
	{
		using var runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);
		await runner.SimulateFrames(2);
		var level = runner.Scene().GetNode<Level>("Level");
		AssertBool(level.CanDescend).IsTrue();
		AssertBool(level.MapGenerator.EnemySpawnPoints.All(p => p.Scenes[0].ResourcePath.Contains("/skeleton/"))).IsTrue();

		level.ConfigureGeneration(GameSession.GetLevelSeed(12345, 2), 2);
		level.Generate();
		await runner.SimulateFrames(1);
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
		nest.TakeDamage(1, 1000, Vector3.Forward, faction: DamageSourceFlags.Player);
		AssertBool(GameSession.Instance.IsEntityCleared(nestId)).IsTrue();
		await runner.SimulateFrames(1);
		level.Generate();
		AssertBool(level.FindChildren("*", "", true, false).OfType<DestructibleNest>()
			.Any(n => n.GetMeta("persistent_id").AsString() == nestId)).IsFalse();

		level.ConfigureGeneration(GameSession.GetLevelSeed(12345, 3), 3);
		level.Generate();
		AssertBool(level.CanDescend).IsFalse();
		var boss = level.FindChildren("*", "", true, false).OfType<EnemyBase>()
			.Single(e => e.GetMeta("persistent_id", "").AsString() == level.Encounters.GatekeeperId);
		AssertFloat(boss.HealthComponent.CurrentHealth).IsEqual(110);
		AssertString(boss.SceneFilePath).IsEqual("res://scenes/enemies/bosses/forsaken_warden.tscn");
		var tree = level.GetTree();
		Node previousScene = tree.CurrentScene;
		tree.CurrentScene = runner.Scene();
		try
		{
			AssertBool(GameSession.Instance.ChangeDungeonDepth(GameSession.LevelTravelDirection.Down)).IsFalse();
			boss.HealthComponent.TakeDamage(1000);
			await Task.Delay(1100); // allow the independent stair-arrival cooldown to expire
			AssertBool(GameSession.Instance.CanUseTransition(GameSession.LevelTravelDirection.Down)).IsTrue();
		}
		finally { tree.CurrentScene = previousScene; }
		AssertBool(level.CanDescend).IsTrue();
		AssertBool(GameSession.Instance.IsEntityCleared(level.Encounters.GatekeeperId)).IsTrue();
		level.Generate();
		AssertBool(level.CanDescend).IsTrue();
		AssertBool(level.FindChildren("*", "", true, false).OfType<EnemyBase>()
			.Any(e => e.GetMeta("persistent_id", "").AsString() == level.Encounters.GatekeeperId)).IsFalse();
		AssertInt(level.MapGenerator.EnemySpawnPoints.Count(p => p.PersistentId == level.Encounters.GatekeeperId)).IsEqual(1);
	}

	[TestCase]
	public async Task CrossbowVariantResolvesShootingClipAndProjectile()
	{
		using var runner = ISceneRunner.Load("res://scenes/enemies/skeleton/skeleton_crossbow.tscn", true);
		await runner.SimulateFrames(2);
		var actor = runner.Scene();
		var animation = actor.GetNode<AttackAnimationComponent>("AttackAnimationComponent");
		AssertBool(animation.TryResolve("ranged", out var playback)).IsTrue();
		AssertString(playback.Clip.ToString()).IsEqual("1H_Ranged_Shoot");
		var behavior = actor.GetNode<EnemyBehaviorComponent>("EnemyBehaviorComponent").Profile;
		AssertBool(behavior.RangedAttackDefinition.IsRanged).IsTrue();
		AssertObject(behavior.RangedAttackDefinition.ProjectileScene).IsNotNull();
		var controller = actor.GetNode<AttackController>("AttackController");
		AssertBool(controller.TryPrepareAttack(behavior.RangedAttackDefinition, "ranged", 1, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, 3, 6, .9f, 0, 4)).IsTrue();
		Node projectileParent = actor.GetTree().CurrentScene ?? actor.GetParent();
		var before = projectileParent.GetChildren().OfType<Projectile>().ToHashSet();
		controller._PhysicsProcess(.5); // cross the authored release time without relying on frame timing
		var arrow = projectileParent.GetChildren().OfType<Projectile>().Single(p => !before.Contains(p));
		AssertFloat(arrow.Speed).IsEqual(16);
		AssertFloat(arrow.MinDamage).IsEqual(3);
		AssertInt((int)arrow.GetNode<HitBoxComponent>("%HitBoxComponent").CollisionMask).IsEqual(4);
		ScenePool.Despawn(arrow);
		controller.CancelAttack();
	}
}