namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Actor-root Inspector properties use the same source-owned statuses as gameplay.</summary>
[TestSuite, RequireGodotRuntime]
public partial class ActorStatusInspectorTest
{
	[TestCase]
	public async Task AuthoredFlagsAndRuntimeChangesWorkOnPlayerAndMonsters()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner, 3);
		foreach (string path in new[] { "player/player", "enemies/skeleton/skeleton_minion", "enemies/skeleton/skeleton_warrior",
			"enemies/skeleton/skeleton_crossbow", "enemies/rat/rat", "enemies/forsaken_warden/forsaken_warden",
			"enemies/templates/melee_enemy", "enemies/templates/projectile_enemy" })
		{
			var actor = GD.Load<PackedScene>($"res://scenes/{path}.tscn").Instantiate<Node3D>();
			actor.Set("Invisible", true); actor.Set("Frozen", true); actor.Set("IronSkin", true); actor.Set("StoneSkin", true);
			TestFactories.AddToSceneTree(actor);
			await runner.SimulateFrames(2);
			foreach (string property in new[] { "Invisible", "Frozen", "IronSkin", "StoneSkin" })
				AssertBool(actor.Get(property).AsBool()).IsTrue();
			var controller = actor.GetNode<BuffController>("BuffController");
			var cloak = actor.GetNode<InvisibilityComponent>("InvisibilityComponent");
			AssertBool(cloak.IsInvisible).IsTrue();
			AssertInt(controller.ActiveBuffs.Count).IsEqual(4);
			AssertBool(actor.GetNode<FrozenStatusComponent>("FrozenStatusComponent").ActionGate.IsStatusLocked).IsTrue();
			foreach (string property in new[] { "Invisible", "Frozen", "IronSkin", "StoneSkin" }) actor.Set(property, false);
			foreach (string property in new[] { "Invisible", "Frozen", "IronSkin", "StoneSkin" })
				AssertBool(actor.Get(property).AsBool()).IsFalse();
			AssertInt(controller.ActiveBuffs.Count).IsEqual(0);
			actor.QueueFree();
			await runner.SimulateFrames(2);
		}
	}

	[TestCase]
	public async Task TurningOffInspectorSourcePreservesOtherSourcesAndGameplayEndsBeforeFade()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		var cloak = player.Invisibility;
		var source = new object();
		player.Invisible = true;
		await runner.SimulateFrames(20);
		AssertBool(player.BuffController.ApplyStatus(cloak.Condition, source, 5)).IsTrue();
		player.Invisible = false;
		AssertBool(player.Invisible).IsTrue();
		AssertBool(cloak.IsInvisible).IsTrue();
		player.BuffController.RemoveStatuses(source);
		AssertBool(player.Invisible).IsFalse();
		AssertBool(cloak.IsInvisible).IsFalse();
		AssertBool(cloak.Host.SurfaceController.HasCloak).IsTrue();
		await runner.SimulateFrames(30);
		AssertBool(cloak.Host.SurfaceController.HasCloak).IsFalse();
	}

	[TestCase]
	public async Task SkinStatesOwnVisualsWithoutAddingUnspecifiedProtection()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		var player = harness.Player;
		float armor = player.HurtBoxComponent.Armor;
		player.IronSkin = true;
		AssertBool(player.IronSkin).IsTrue();
		AssertInt(player.WorldEffects.LeaseCount).IsEqual(1);
		player.StoneSkin = true;
		AssertBool(player.StoneSkin).IsTrue();
		AssertInt(player.WorldEffects.LeaseCount).IsEqual(2);
		AssertFloat(player.HurtBoxComponent.Armor).IsEqual(armor);
		player.IronSkin = false; player.StoneSkin = false;
		AssertInt(player.WorldEffects.LeaseCount).IsEqual(0);
	}

	[TestCase]
	public async Task AuthoredAdditionalConditionsUseActorOwnedSources()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner, 3);
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		var frozen = GD.Load<StatusCondition>("res://scenes/buffs/conditions/frozen.tres");
		player.InitialConditions.Add(frozen);
		TestFactories.AddToSceneTree(player);
		await runner.SimulateFrames(2);
		AssertBool(player.Frozen).IsTrue();
		player.Frozen = false;
		AssertBool(player.Frozen).IsTrue();
		player.BuffController.RemoveStatuses(player);
		AssertBool(player.Frozen).IsFalse();
		var enemy = GD.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn").Instantiate<EnemyBase>();
		enemy.InitialConditions.Add(frozen);
		TestFactories.AddToSceneTree(enemy);
		await runner.SimulateFrames(2);
		AssertBool(enemy.Frozen).IsTrue();
		enemy.Frozen = false;
		AssertBool(enemy.Frozen).IsTrue();
		enemy.BuffController.RemoveStatuses(enemy);
		AssertBool(enemy.Frozen).IsFalse();
	}
}
