namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The player's use-item gesture (potions, fountains) follows player state like every other animation: it starts
/// at once even mid-walk, and once the action ends, walking takes over instead of the player sliding through the
/// rest of the clip.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UseItemAnimationTest
{
	[TestCase]
	public async Task GestureStartsMidWalkAndYieldsToWalkingOnceTheActionEnds()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		// Far from the harness's fixtures, on a floor of its own.
		var ground = new StaticBody3D { Position = new Vector3(200, -0.5f, 200) };
		ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) } });
		TestFactories.AddToSceneTree(ground);
		var player = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.Position = new Vector3(200, 0, 200);
		TestFactories.AddToSceneTree(player);
		player.InputComponent.DeviceEnabled = false;
		var playback = (AnimationNodeStateMachinePlayback)player.GetNode<AnimationTree>("AnimationTree").Get("parameters/playback");
		await runner.SimulateFrames(5, 20);

		player.InputComponent.InputDirection = Vector3.Left;
		await TestFactories.AwaitPhysicsUntil(runner, () => playback.GetCurrentNode() == "Walking", 1);
		// Settle the 0.3-second blend into walking: the tree takes no transition mid-blend.
		await runner.SimulateFrames(20, 20);
		player.ActionManager.AssignAction(0, new ConsumableItem(), null, 1);
		player.ActionManager.TryPerformAction(0);
		await runner.SimulateFrames(2, 20);
		AssertString(playback.GetCurrentNode()).IsEqual("Use Item");

		// The clip runs 1.6 seconds; the action ends long before, and the held direction resumes walking.
		await TestFactories.AwaitPhysicsUntil(runner, () => !player.IsPerformingAction, 1);
		await runner.SimulateFrames(3, 20);
		AssertString(playback.GetCurrentNode()).IsEqual("Walking");
	}
}
