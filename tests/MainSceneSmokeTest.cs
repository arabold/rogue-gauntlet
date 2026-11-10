namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The gameplay scene boots through the whole startup pipeline (autoloads, level generation and the spawn point placing
/// the player) and its camera answers the real input. <see cref="DarkLevelTest"/> also boots it and drives the debug menu.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainSceneSmokeTest
{
	/// <summary>The booted scene has a player; zoom limits and simultaneous orbiting work through the real input and camera wiring.</summary>
	[TestCase]
	public async Task MainSceneSpawnsThePlayerAndCameraZoomClampsWhileRotating()
	{
		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(10, 20);
		var main = (Main)runner.Scene();
		var player = (Node3D)main.GetTree().GetFirstNodeInGroup("player");
		AssertObject(player).OverrideFailureMessage("Generation spawned no player").IsNotNull();
		main.SetProcess(false);
		Camera3D camera = main.GameCamera;
		var pcam = main.GetNode<Node3D>("PhantomCamera3D");
		AssertFloat(camera.Size).IsEqualApprox(20f, 0.001f);

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
			AssertBool((pcam.GlobalPosition - player.GlobalPosition).Normalized().IsEqualApprox(pcam.GlobalBasis.Z)).IsTrue();
			main.SetProcess(true);
			await runner.SimulateFrames(3, 20);
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