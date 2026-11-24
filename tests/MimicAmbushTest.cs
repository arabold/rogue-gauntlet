namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// A chest mimic waits as a chest: still, out of the hero's targeting, until the hero opens it or lingers beside it,
/// and then it attacks. From the Prison on, a placed chest may be one.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MimicAmbushTest
{
	private const string Mimic = "res://scenes/enemies/mimic/chest_mimic.tscn";

	[TestCase]
	public async Task ItWaitsAsAChestUntilOpenedThenAttacks()
	{
		var (runner, harness, mimic) = await Stage(2.2f);
		using var cleanup = runner;
		var player = harness.Player;
		Vector3 start = mimic.GlobalPosition;
		await runner.SimulateFrames(30, 20);
		AssertBool(mimic.AmbushComponent.IsDormant).IsTrue();
		AssertBool(mimic.IsInGroup("enemy")).IsFalse();
		AssertFloat(mimic.GlobalPosition.HorizontalDistanceTo(start)).IsLess(0.05f);

		var chest = mimic.FindChildren("*", "", true, false).OfType<Chest>().Single();
		chest.Interactive.Interact(player);
		await TestFactories.AwaitPhysicsUntil(runner, () => mimic.AmbushComponent.IsAwake, 3);
		AssertBool(GodotObject.IsInstanceValid(chest) && chest.IsInsideTree()).IsFalse();
		float health = player.HealthComponent.CurrentHealth;
		await TestFactories.AwaitPhysicsUntil(runner, () => player.HealthComponent.CurrentHealth < health, 6);
		AssertBool(mimic.IsInGroup("enemy")).IsTrue();
	}

	[TestCase]
	public async Task LingeringBesideItWakesIt()
	{
		var (runner, _, mimic) = await Stage(1.4f);
		using var cleanup = runner;
		await runner.SimulateFrames(40, 20);
		AssertBool(mimic.AmbushComponent.IsAwake).IsFalse();
		await TestFactories.AwaitPhysicsUntil(runner, () => mimic.AmbushComponent.IsAwake, mimic.AmbushComponent.WakeDelay + 1);
	}

	[TestCase]
	public async Task EveryChestCanBeAMimicFromThePrisonOn()
	{
		using var runner = TestFactories.LoadScene("res://scenes/levels/level.tscn");
		await runner.SimulateFrames(2, 20);
		var level = (Level)runner.Scene();
		var prison = (RealmEncounterFactory)((RealmEncounterFactory)level.MapGenerator.MobFactory).Fallback.Duplicate();
		prison.MimicChance = 1;
		level.MapGenerator.MobFactory = prison;
		level.ConfigureGeneration(new RunState(23), 7, null, null);
		level.Generate();
		var mimics = level.MapGenerator.EnemySpawnPoints.Where(spawn => spawn.PersistentId.Contains(":chest:")).ToList();
		AssertBool(mimics.Count > 0 && mimics.All(spawn => spawn.Scenes.Single().ResourcePath == Mimic)).IsTrue();
		AssertInt(level.MapGenerator.FloorContent.FindChildren("*", "", true, false).OfType<Chest>().Count()).IsEqual(0);
		await runner.SimulateFrames(1, 20);
	}

	/// <summary>A hero standing still with a mimic <paramref name="distance"/> ahead, its monster AI on.</summary>
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, EnemyBase Mimic)> Stage(float distance)
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		var mimic = TestFactories.SpawnHarnessMonster(harness, Mimic, distance);
		harness.ExecuteCommand("automation", value: 1);
		await runner.SimulateFrames(2, 20);
		return (runner, harness, mimic);
	}
}
