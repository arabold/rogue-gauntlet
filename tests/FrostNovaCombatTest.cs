namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Frost Nova uses real actor contacts, visibility and source-owned frozen statuses.</summary>
[TestSuite, RequireGodotRuntime]
public partial class FrostNovaCombatTest
{
	private sealed class ChipBlock : IDefenseEvaluator
	{
		public bool TryEvaluate(in DamageContext contact, out DefenseEvaluation evaluation)
		{
			evaluation = new DefenseEvaluation(DamageOutcome.Blocked, 1, false, true);
			return (contact.DefenseTraits & AttackDefenseTraits.Guardable) != 0;
		}
	}

	[TestCase]
	public async Task AreaHitsEachVisibleEnemyOnceAndStopsAtWalls()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var root = runner.Scene();
		harness.Player.GlobalPosition = Vector3.Zero;
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var first = TestFactories.SpawnCombatMonster(root, new Vector3(2, 0, 0), "First");
		var second = TestFactories.SpawnCombatMonster(root, new Vector3(0, 0, 2), "Second");
		var blocked = TestFactories.SpawnCombatMonster(root, new Vector3(-2, 0, 0), "Blocked");
		var distant = TestFactories.SpawnCombatMonster(root, new Vector3(7, 0, 0), "Distant");
		var wall = new StaticBody3D { Position = new Vector3(-1, 1, 0), CollisionLayer = 1 };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.2f, 2, 2) } });
		root.AddChild(wall);
		await runner.SimulateFrames(5);
		foreach (var actor in new[] { first, second, blocked, distant }) TestFactories.PrepareCombatMonster(actor);
		float playerHealth = harness.Player.HealthComponent.CurrentHealth;
		var frost = harness.GetSpell<FrostNovaSpell>();
		AssertBool(frost.Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 1 })).IsTrue();
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(47);
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(47);
		AssertFloat(blocked.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(distant.HealthComponent.CurrentHealth).IsEqual(50);
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(playerHealth);
		AssertBool(first.BuffController.HasCondition(frost.Definition.Frozen)).IsTrue();
		AssertBool(second.BuffController.HasCondition(frost.Definition.Frozen)).IsTrue();
		AssertBool(blocked.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
		var visual = root.GetChildren().OfType<WaterSpellVisual>().Single();
		AssertFloat(visual.PreviewRadius).IsEqualApprox(3.5, .001);
		AssertBool(((ShaderMaterial)visual.Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		frost.Cancel();
		AssertBool(first.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
		AssertBool(second.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task InvulnerableAndKilledTargetsAreNotFrozen()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		var immune = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Immune");
		var lethal = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, 2), "Lethal");
		var guarded = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, -2), "Guarded");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(immune); TestFactories.PrepareCombatMonster(lethal); TestFactories.PrepareCombatMonster(guarded);
		immune.HurtBoxComponent.Invulnerable = true;
		guarded.HurtBoxComponent.RegisterDefenseEvaluator(new ChipBlock());
		lethal.HealthComponent.SetHealth(2, 50);
		AssertBool(harness.GetSpell<FrostNovaSpell>().Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 2 })).IsTrue();
		AssertFloat(immune.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(immune.BuffController.HasCondition(harness.GetSpell<FrostNovaSpell>().Definition.Frozen)).IsFalse();
		AssertBool(lethal.HealthComponent.IsDead).IsTrue();
		AssertBool(lethal.BuffController.HasCondition(harness.GetSpell<FrostNovaSpell>().Definition.Frozen)).IsFalse();
		AssertFloat(guarded.HealthComponent.CurrentHealth).IsEqual(49);
		AssertBool(guarded.BuffController.HasCondition(harness.GetSpell<FrostNovaSpell>().Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task RecastingRefreshesAndCancelPreservesOtherSources()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Monster");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(monster);
		var random = new RandomNumberGenerator { Seed = 3 };
		var frost = harness.GetSpell<FrostNovaSpell>();
		var other = new object();
		monster.BuffController.ApplyStatus(frost.Definition.Frozen, other, 8);
		AssertBool(frost.Cast(harness.Player, 1, random)).IsTrue();
		AssertBool(frost.Cast(harness.Player, 2, random)).IsTrue();
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(43);
		AssertInt(monster.BuffController.ActiveBuffs.Count).IsEqual(2);
		AssertFloat(monster.BuffController.ActiveBuffs.First(buff => ReferenceEquals(buff.Source, frost)).RemainingDuration).IsEqual(2);
		frost.Cancel();
		AssertBool(monster.BuffController.HasCondition(frost.Definition.Frozen)).IsTrue();
		AssertInt(monster.BuffController.ActiveBuffs.Count).IsEqual(1);
		monster.BuffController.RemoveStatuses(other);
		AssertBool(monster.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task CancelDuringStatusNotificationStopsRemainingContacts()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		var first = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "AFirst");
		var second = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(0, 0, 2), "BSecond");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(first); TestFactories.PrepareCombatMonster(second);
		var frost = harness.GetSpell<FrostNovaSpell>();
		first.BuffController.BuffsChanged += () =>
		{
			if (first.BuffController.HasCondition(frost.Definition.Frozen)) frost.Cancel();
		};
		AssertBool(frost.Cast(harness.Player, 1, new RandomNumberGenerator { Seed = 4 })).IsTrue();
		AssertFloat(first.HealthComponent.CurrentHealth).IsEqual(47);
		AssertFloat(second.HealthComponent.CurrentHealth).IsEqual(50);
		AssertBool(first.BuffController.HasCondition(frost.Definition.Frozen)).IsFalse();
	}

	[TestCase]
	public async Task HarnessButtonReleasesSelectedLevelDuringPhysics()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(5.5f, 0, 0), "Monster");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(monster);
		TestFactories.CastHarnessSpell(harness, "Frost Nova", 3);
		await runner.SimulateFrames(3);
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(45);
		AssertBool(monster.BuffController.HasCondition(harness.GetSpell<FrostNovaSpell>().Definition.Frozen)).IsTrue();
		var visual = runner.Scene().GetChildren().OfType<WaterSpellVisual>().Single();
		AssertFloat(visual.PreviewRadius).IsEqualApprox(6.5, .001);
	}

	[TestCase]
	public async Task MonsterNovaCanDamageAndFreezeThePlayer()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.GlobalPosition = Vector3.Zero;
		harness.Player.HurtBoxComponent.Armor = 0;
		var monster = TestFactories.SpawnCombatMonster(runner.Scene(), new Vector3(2, 0, 0), "Caster");
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(monster);
		float before = harness.Player.HealthComponent.CurrentHealth;
		AssertBool(harness.GetSpell<FrostNovaSpell>().Cast(monster, 1, new RandomNumberGenerator { Seed = 5 })).IsTrue();
		AssertFloat(harness.Player.HealthComponent.CurrentHealth).IsEqual(before - 3);
		AssertBool(harness.Player.BuffController.HasCondition(harness.GetSpell<FrostNovaSpell>().Definition.Frozen)).IsTrue();
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(50);
	}
}
