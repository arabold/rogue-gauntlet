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
		ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");

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

	/// <summary>Exercises zoom limits and simultaneous orbiting through the real gameplay input and camera wiring.</summary>
	[TestCase]
	public async Task CameraZoomClampsAndWorksWhileRotating()
	{
		ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(30);
		var main = (Main)runner.Scene();
		main.SetProcess(false);
		Camera3D camera = main.GameCamera;
		var pcam = main.GetNode<Node3D>("PhantomCamera3D");
		AssertFloat(camera.Size).IsEqualApprox(20f, 0.001f);
		AssertBool(InputMap.ActionGetEvents("camera_zoom_in")[0] is InputEventKey { PhysicalKeycode: Key.Apostrophe }).IsTrue();
		AssertBool(InputMap.ActionGetEvents("camera_zoom_out")[0] is InputEventKey { PhysicalKeycode: Key.Slash }).IsTrue();

		try
		{
			Basis before = pcam.GlobalBasis;
			Input.ActionPress("camera_zoom_in");
			Input.ActionPress("camera_rotate_right");
			main._Process(0.25);
			AssertFloat(camera.Size).IsEqualApprox(17f, 0.001f);
			AssertBool(pcam.GlobalBasis.IsEqualApprox(before)).IsFalse();
			main._Process(2);
			AssertFloat(camera.Size).IsEqualApprox(10f, 0.001f);
			Input.ActionRelease("camera_rotate_right");
			Input.ActionPress("camera_zoom_out");
			main._Process(0.25);
			AssertFloat(camera.Size).IsEqualApprox(10f, 0.001f);
			Input.ActionRelease("camera_zoom_in");
			main._Process(0.25);
			AssertFloat(camera.Size).IsEqualApprox(13f, 0.001f);
			main._Process(3);
			AssertFloat(camera.Size).IsEqualApprox(32f, 0.001f);
			Input.ActionRelease("camera_zoom_out");
			var player = (Node3D)main.GetTree().GetFirstNodeInGroup("player");
			AssertBool((pcam.GlobalPosition - player.GlobalPosition).Normalized().IsEqualApprox(pcam.GlobalBasis.Z)).IsTrue();
			main.SetProcess(true);
			await runner.SimulateFrames(3);
			AssertFloat(camera.Size).IsEqualApprox(32f, 0.001f);
		}
		finally
		{
			Input.ActionRelease("camera_zoom_in");
			Input.ActionRelease("camera_zoom_out");
			Input.ActionRelease("camera_rotate_right");
		}
	}
}