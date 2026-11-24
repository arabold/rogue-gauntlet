namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The nymph's touch steals one item stack the hero is not wearing, and she runs off with it: killing her first drops
/// it at her feet, staying out of the hero's reach long enough lets her vanish with it for good, and a hero who keeps
/// up sees her blink away unless a stagger catches her mid-fade.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NymphTheftTest
{
	private const string Nymph = "res://scenes/enemies/nymph/nymph.tscn";
	private const string Potion = "res://scenes/items/potions/potion_healing_small.tres";

	[TestCase]
	public async Task SheStealsALooseStackRunsOffAndDropsItWhenKilled()
	{
		var (runner, harness, nymph) = await Stage();
		using var cleanup = runner;
		var inventory = harness.Player.Inventory;
		var worn = inventory.Items.Where(inventory.IsEquipped).ToList();
		var loose = inventory.Items.Where(entry => !inventory.IsEquipped(entry)).ToList();

		await TestFactories.AwaitPhysicsUntil(runner, () => nymph.TheftComponent.IsCarrying, 10);
		var stolen = loose.Where(entry => !inventory.Items.Contains(entry)).ToList();
		AssertInt(stolen.Count).IsEqual(1);
		foreach (var entry in worn) AssertBool(inventory.IsEquipped(entry)).IsTrue();
		AssertBool(Told("stole your")).IsTrue();

		// She runs out of reach instead of fighting on.
		await TestFactories.AwaitPhysicsUntil(runner,
			() => nymph.GlobalPosition.HorizontalDistanceTo(harness.Player.GlobalPosition) > 6, 4);
		TestFactories.Tool<MonsterTool>(runner.Scene()).KillAll();
		await TestFactories.AwaitPhysicsUntil(runner, () => Pickups(runner).Any(pickup => pickup.Item == stolen[0].Item), 3);
	}

	[TestCase]
	public async Task SheVanishesWithTheLootOnceOutOfReachLongEnough()
	{
		var (runner, harness, nymph) = await Stage();
		using var cleanup = runner;
		nymph.TheftComponent.EscapeDistance = 4;
		nymph.TheftComponent.EscapeTime = 0.5f;
		var inventory = harness.Player.Inventory;
		var loose = inventory.Items.Where(entry => !inventory.IsEquipped(entry)).ToList();

		await TestFactories.AwaitPhysicsUntil(runner, () => nymph.TheftComponent.IsCarrying, 10);
		var stolen = loose.Single(entry => !inventory.Items.Contains(entry)).Item;
		await TestFactories.AwaitPhysicsUntil(runner, () => !GodotObject.IsInstanceValid(nymph), 8);
		AssertBool(inventory.Items.Any(entry => entry.Item == stolen)).IsFalse();
		AssertBool(Pickups(runner).Any(pickup => pickup.Item == stolen)).IsFalse();
		AssertBool(Told("got away with your")).IsTrue();
	}

	[TestCase]
	public async Task SheBlinksAwayFromAHeroWhoKeepsUp()
	{
		var (runner, harness, nymph) = await Stage();
		using var cleanup = runner;
		nymph.TheftComponent.EscapeTime = 20;
		await TestFactories.AwaitPhysicsUntil(runner, () => nymph.TheftComponent.IsCarrying, 10);
		Vector3 last = nymph.GlobalPosition;
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			bool blinked = nymph.GlobalPosition.HorizontalDistanceTo(last) > 3;
			last = nymph.GlobalPosition;
			if (!blinked) KeepUp(harness.Player, nymph);
			return blinked;
		}, 5);
		AssertFloat(nymph.GlobalPosition.HorizontalDistanceTo(harness.Player.GlobalPosition)).IsGreaterEqual(6.5f);
		AssertBool(nymph.TheftComponent.IsCarrying).IsTrue();
	}

	[TestCase]
	public async Task AStaggerDuringHerFadeKeepsHerInPlace()
	{
		var (runner, harness, nymph) = await Stage();
		using var cleanup = runner;
		nymph.TheftComponent.EscapeTime = 20;
		await TestFactories.AwaitPhysicsUntil(runner, () => nymph.TheftComponent.IsCarrying, 10);
		// The nymph has no dash, so a dash-kind action is her blink's fade.
		await TestFactories.AwaitPhysicsUntil(runner, () =>
		{
			KeepUp(harness.Player, nymph);
			return nymph.ActionGate.CurrentKind == ActorActionKind.Dash;
		}, 5);
		AssertBool(nymph.TryStagger(.4f)).IsTrue();
		float jump = 0;
		Vector3 last = nymph.GlobalPosition;
		for (int frame = 0; frame < 30; frame++)
		{
			await runner.SimulateFrames(1, 20);
			jump = Mathf.Max(jump, nymph.GlobalPosition.HorizontalDistanceTo(last));
			last = nymph.GlobalPosition;
		}
		AssertFloat(jump).IsLess(1f);
	}

	/// <summary>Moves the hero to within 2 m of the nymph, as a hero chasing her would.</summary>
	private static void KeepUp(Player hero, EnemyBase nymph)
	{
		Vector3 at = nymph.GlobalPosition;
		if (hero.GlobalPosition.HorizontalDistanceTo(at) <= 2.5f || !(hero.GlobalPosition - at).TryNormalizeHorizontal(out var side)) return;
		hero.GlobalPosition = at + side * 2;
		hero.ResetPhysicsInterpolation();
	}

	/// <summary>A hero standing still with a potion in the pack, and a nymph beside the hero deciding for herself.</summary>
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, EnemyBase Nymph)> Stage()
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		GameLog.ClearPlayerHistory();
		var items = TestFactories.Tool<ItemSpawnTool>(runner.Scene());
		AssertBool(items.Give(items.Create(TestFactories.Load<Item>(Potion)), false)).IsTrue();
		var nymph = TestFactories.SpawnHarnessMonster(harness, Nymph, 2.5f);
		harness.ExecuteCommand("automation", value: 1);
		return (runner, harness, nymph);
	}

	/// <summary>Whether the hero's message feed reported <paramref name="text"/>.</summary>
	private static bool Told(string text) => GameLog.GetPlayerHistory().Any(entry => entry.Message.Contains(text));

	private static IEnumerable<LootableItem> Pickups(ISceneRunner runner) =>
		runner.Scene().FindChildren("*", "", true, false).OfType<LootableItem>();
}
