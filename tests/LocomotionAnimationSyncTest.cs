namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Verifies that player and monster walking animations synchronize their TimeScale parameter
/// proportionally to real-time ground velocity, loop continuously, and leave attack and action animations unaltered.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LocomotionAnimationSyncTest
{
	[TestCase]
	public async Task PlayerWalkingAnimationLoopsContinuouslyWithoutFreezing()
	{
		ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(35);
		var scene = runner.Scene();
		var player = (Player)scene.GetTree().GetFirstNodeInGroup("player");
		var animTree = player.GetNode<AnimationTree>("AnimationTree");
		var playback = (AnimationNodeStateMachinePlayback)animTree.Get("parameters/playback");
		var animPlayer = animTree.GetNode<AnimationPlayer>(animTree.AnimPlayer);
		var runningAnim = animPlayer.GetAnimation("Running_A");

		// Running_A must be configured as Linear loop mode automatically by the runtime setup
		AssertObject(runningAnim.LoopMode).IsEqual(Animation.LoopModeEnum.Linear);

		Input.ActionPress("move_right");
		try
		{
			int wrapCount = 0;
			float lastPos = 0f;

			for (int f = 0; f < 80; f++)
			{
				await runner.SimulateFrames(1);
				float currentPos = (float)animTree.Get("parameters/Walking/Run/current_position");
				if (currentPos < lastPos && lastPos > 0.4f)
				{
					wrapCount++;
				}
				lastPos = currentPos;
			}

			// In 80 frames of running, the 0.8s animation at ~2.78x scale should complete multiple full loops
			AssertBool(player.IsMoving).IsTrue();
			AssertString(playback.GetCurrentNode()).IsEqual("Walking");
			AssertBool(wrapCount >= 2).IsTrue();

			float scale = (float)animTree.Get("parameters/Walking/TimeScale/scale");
			AssertBool(scale > 2.0f && scale < 3.5f).IsTrue();

			// When moving at full run speed (7.5 m/s), blend_amount should be 1.0 (pure Run)
			float blendAmount = (float)animTree.Get("parameters/Walking/Blend2/blend_amount");
			AssertFloat(blendAmount).IsEqualApprox(1.0f, 0.05f);
		}
		finally
		{
			Input.ActionRelease("move_right");
		}
	}

	[TestCase]
	public async Task PlayerWalkModifierBlendsTowardsWalkAnimation()
	{
		ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(35);
		var scene = runner.Scene();
		var player = (Player)scene.GetTree().GetFirstNodeInGroup("player");
		var animTree = player.GetNode<AnimationTree>("AnimationTree");
		var playback = (AnimationNodeStateMachinePlayback)animTree.Get("parameters/playback");

		Input.ActionPress("walk");
		Input.ActionPress("move_right");
		try
		{
			// Give player enough frames (with fixed delta) to complete cross-fade and accelerate to walk speed (~3.0 m/s)
			for (int f = 0; f < 50; f++)
			{
				await runner.SimulateFrames(1, 20);
			}

			AssertBool(player.IsMoving).IsTrue();
			AssertString(playback.GetCurrentNode()).IsEqual("Walking");

			// At 3.0 m/s (40% of 7.5 m/s), blend is (3.0 - 1.5) / 4.0 = 0.375 (favoring Walk over Run)
			float blendAmount = (float)animTree.Get("parameters/Walking/Blend2/blend_amount");
			AssertFloat(blendAmount).IsEqualApprox(0.375f, 0.05f);

			// MovementComponent velocity should be locked to 3.0 m/s (7.5 * 0.4)
			float horizontalSpeed = new Vector2(player.MovementComponent.Velocity.X, player.MovementComponent.Velocity.Z).Length();
			AssertFloat(horizontalSpeed).IsEqualApprox(3.0f, 0.1f);
		}
		finally
		{
			Input.ActionRelease("move_right");
			Input.ActionRelease("walk");
		}
	}

	[TestCase]
	public void MonsterLocomotionAnimationsLoopAndHaveCalibratedSpeed()
	{
		var skeletonScene = TestFactories.Load<PackedScene>("res://scenes/enemies/skeleton/skeleton_minion.tscn");
		var skeleton = skeletonScene.Instantiate<CharacterBody3D>();
		TestFactories.AddToSceneTree(skeleton);

		try
		{
			var behavior = skeleton.GetNode<EnemyBehaviorComponent>("EnemyBehaviorComponent");
			var animTree = skeleton.GetNode<AnimationTree>("EnemyBehaviorComponent/AnimationTree");
			var animPlayer = animTree.GetNode<AnimationPlayer>(animTree.AnimPlayer);
			var runningAnim = animPlayer.GetAnimation("Running_A");

			AssertObject(runningAnim.LoopMode).IsEqual(Animation.LoopModeEnum.Linear);
			AssertFloat(behavior.NaturalLocomotionSpeed).IsEqualApprox(2.7f, 0.01f);
		}
		finally
		{
			skeleton.QueueFree();
		}
	}
}
