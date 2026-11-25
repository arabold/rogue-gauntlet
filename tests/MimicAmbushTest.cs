namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// A chest mimic waits as a chest: still, concealed (no nameplate, monster x-ray or aim-assist), until the hero opens it
/// or lingers beside it, and then it attacks. From the Prison on, a placed chest may be one.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MimicAmbushTest
{
	private const string Mimic = "res://scenes/enemies/mimic/chest_mimic.tscn";

	[TestCase]
	public async Task ItWaitsAsAChestUntilOpenedThenAttacks()
	{
		var (runner, harness, mimic) = await TestFactories.StageHarnessMonster(Mimic, 2.2f);
		using var cleanup = runner;
		var player = harness.Player;
		Vector3 start = mimic.GlobalPosition;
		await runner.SimulateFrames(30, 20);
		AssertBool(mimic.IsConcealed).IsTrue();
		AssertBool(mimic.Nameplate.IsShown || mimic.Xray.IsShown).IsFalse();
		AssertFloat(mimic.GlobalPosition.HorizontalDistanceTo(start)).IsLess(0.05f);

		var chest = mimic.FindChildren("*", "", true, false).OfType<Chest>().Single();
		chest.Interactive.Interact(player);
		await TestFactories.AwaitPhysicsUntil(runner, () => mimic.AmbushComponent.IsAwake, 3);
		AssertBool(GodotObject.IsInstanceValid(chest) && chest.IsInsideTree()).IsFalse();
		float health = player.HealthComponent.CurrentHealth;
		await TestFactories.AwaitPhysicsUntil(runner, () => player.HealthComponent.CurrentHealth < health, 6);
		AssertBool(mimic.IsConcealed).IsFalse();
		AssertBool(mimic.Nameplate.IsShown && mimic.Xray.IsShown).IsTrue();
	}

	[TestCase]
	public async Task LingeringBesideItWakesIt()
	{
		var (runner, _, mimic) = await TestFactories.StageHarnessMonster(Mimic, 1.4f);
		using var cleanup = runner;
		await runner.SimulateFrames(40, 20);
		AssertBool(mimic.AmbushComponent.IsAwake).IsFalse();
		await TestFactories.AwaitPhysicsUntil(runner, () => mimic.AmbushComponent.IsAwake, mimic.AmbushComponent.WakeDelay + 1);
	}

	[TestCase]
	public async Task EveryChestCanBeAMimicFromThePrisonOn()
	{
		var (runner, level) = await TestFactories.GenerateLevel(23, 7, configured =>
		{
			var prison = (RealmEncounterFactory)((RealmEncounterFactory)configured.MapGenerator.MobFactory).Fallback.Duplicate();
			prison.MimicChance = 1;
			configured.MapGenerator.MobFactory = prison;
		});
		using var cleanup = runner;
		var mimics = level.MapGenerator.EnemySpawnPoints.Where(spawn => spawn.PersistentId.Contains(":chest:")).ToList();
		AssertBool(mimics.Count > 0 && mimics.All(spawn => spawn.Scenes.Single().ResourcePath == Mimic)).IsTrue();
		// Locked and secret rooms' reward chests are never mimics: a key or a found secret always pays.
		AssertInt(level.MapGenerator.FloorContent.FindChildren("*", "", true, false).OfType<Chest>()
			.Count(chest => chest.SceneFilePath == "res://scenes/props/chest.tscn")).IsEqual(0);
		await runner.SimulateFrames(1, 20);
	}
}
