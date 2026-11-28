namespace RogueGauntlet.Tests;

using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The debug menu's playtest log describes a run for the balance report: each floor with its monsters, the hero, what
/// the hero picks up, and every kill with the experience it paid. It outlives the gameplay scene, which a floor change
/// reloads, so one file covers every floor.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlaytestRecorderTest
{
	private const string MainScene = "res://scenes/main/main.tscn";

	[TestCase]
	public async Task OneLogFollowsTheRunAcrossFloors()
	{
		string path;
		uint depth;
		using (ISceneRunner runner = TestFactories.LoadScene(MainScene))
		{
			await TestFactories.AwaitPhysicsFrames(runner, 5);
			Node main = runner.Scene();
			depth = main.GetNode<Level>("Level").Depth;
			TestFactories.Tool<PlaytestTool>(main).Recording = true;
			path = PlaytestRecorder.Current.OutputPath;
			await TestFactories.AwaitPhysicsFrames(runner, 2);

			var items = TestFactories.Tool<ItemSpawnTool>(main);
			AssertBool(items.Give(items.Create(TestFactories.Load<Item>("res://scenes/items/potions/potion_healing_small.tres")), false)).IsTrue();
			TestFactories.Tool<MonsterTool>(main).KillAll();
		}
		using (ISceneRunner runner = TestFactories.LoadScene(MainScene))
		{
			await TestFactories.AwaitPhysicsFrames(runner, 5);
			PlaytestRecorder.Stop();
			await TestFactories.AwaitPhysicsFrames(runner, 1);
		}

		try
		{
			var records = File.ReadAllLines(path).Select(line => JsonDocument.Parse(line).RootElement).ToList();
			JsonElement[] Events(string name) => records.Where(r => r.GetProperty("event").GetString() == name).ToArray();
			JsonElement Data(JsonElement record) => record.GetProperty("data");

			var floors = Events("floor.entered");
			AssertInt(floors.Length).IsEqual(2);
			AssertBool(Data(floors[0]).GetProperty("depth").GetUInt32() == depth).IsTrue();
			AssertInt(Data(floors[0]).GetProperty("monsters").GetArrayLength()).IsGreater(0);
			AssertBool(Events("hero.snapshot").Any(r => Data(r).GetProperty("reason").GetString() == "spawned")).IsTrue();

			var pickup = Data(Events("item.picked_up").Single()).GetProperty("item");
			AssertString(pickup.GetProperty("type_id").GetString()).IsEqual("potion.healing");
			AssertString(pickup.GetProperty("form").GetString()).IsEqual("small");

			var kills = Events("actor.died");
			AssertInt(kills.Length).IsGreater(0);
			AssertBool(kills.All(r => Data(r).GetProperty("xp").GetInt32() > 0)).IsTrue();
			AssertInt(Events("hero.xp").Sum(r => Data(r).GetProperty("xp").GetInt32()))
				.IsEqual(kills.Sum(r => Data(r).GetProperty("xp").GetInt32()));
			AssertString(records[^1].GetProperty("event").GetString()).IsEqual("run.ended");
		}
		finally
		{
			File.Delete(path);
		}
	}
}
