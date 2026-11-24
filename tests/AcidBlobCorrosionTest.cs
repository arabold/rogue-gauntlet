namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Striking an acid blob in melee burns the hero with acid; shooting it from a distance is safe.</summary>
[TestSuite]
[RequireGodotRuntime]
public class AcidBlobCorrosionTest
{
	private const string Blob = "res://scenes/enemies/slime/acid_blob.tscn";
	private const string Crossbow = "res://scenes/items/weapons/crossbow_common.tres";
	private const string Sword = "res://scenes/items/weapons/shortsword_common.tres";

	[TestCase]
	public async Task MeleeHitsBurnTheHeroAndRangedHitsDoNot()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.ExecuteCommand("automation", value: 0);
		var blob = TestFactories.SpawnHarnessMonster(harness, Blob, 1.6f);
		var burn = TestFactories.Load<Buff>("res://scenes/buffs/acid_burn.tres");
		var buffs = harness.Player.BuffController;

		AssertBool(harness.ExecuteCommand("equip", Crossbow)).IsTrue();
		float health = blob.HealthComponent.CurrentHealth;
		harness.Attack();
		await TestFactories.AwaitPhysicsUntil(runner, () => blob.HealthComponent.CurrentHealth < health, 3);
		AssertBool(buffs.HasEquivalent(burn)).IsFalse();

		AssertBool(harness.ExecuteCommand("equip", Sword)).IsTrue();
		await runner.SimulateFrames(30, 20);
		health = blob.HealthComponent.CurrentHealth;
		harness.Attack();
		await TestFactories.AwaitPhysicsUntil(runner, () => blob.HealthComponent.CurrentHealth < health, 3);
		AssertBool(buffs.HasEquivalent(burn)).IsTrue();
	}
}
