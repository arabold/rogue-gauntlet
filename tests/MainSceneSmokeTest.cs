namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// End-to-end smoke test for the gameplay scene. Booting <c>main.tscn</c> exercises
/// the whole startup pipeline — autoloads, level generation, navigation bake, and the
/// authored spawn point placing the player — so a crash anywhere along that path
/// surfaces here rather than only in a manual play session.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainSceneSmokeTest
{
	[TestCase]
	public async Task MainSceneGeneratesLevelAndSpawnsPlayer()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/main/main.tscn", true);

		// Main._Ready generates the level and the spawn point adds the player during
		// generation; give it a handful of frames to settle.
		await runner.SimulateFrames(30);

		Node scene = runner.Scene();
		AssertObject(scene).IsNotNull();

		// The authored Level node owns the MapGenerator that builds the dungeon.
		var level = scene.GetNodeOrNull<Level>("Level");
		AssertObject(level).IsNotNull();

		// A player in the "player" group means generation + spawn ran without crashing.
		Godot.Collections.Array<Node> players = scene.GetTree().GetNodesInGroup("player");
		AssertInt(players.Count).IsGreater(0);
	}
}