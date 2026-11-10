namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Verifies that player and monster walking animations synchronize their TimeScale parameter
/// proportionally to real-time ground velocity and loop continuously.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LocomotionAnimationSyncTest
{
	private static void PlaceOnOpenFloor(Node scene, Player player)
	{
		const float center = 500;
		var floor = new StaticBody3D
		{
			Position = new Vector3(center, -0.1f, center),
			CollisionLayer = 1,
			CollisionMask = 0,
		};
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 0.2f, 100) } });
		scene.AddChild(floor);
		player.GlobalPosition = new Vector3(center, 0, center);
		player.ResetPhysicsInterpolation();
	}

	/// <summary>
	/// Running loops the run clip continuously at a time scale synced to ground speed, the walk modifier holds walking
	/// speed and blends toward the walk clip, and running into walls on every side settles into idle.
	/// </summary>
	[TestCase]
	public async Task PlayerLocomotionLoopsBlendsToWalkingAndIdlesAgainstWalls()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var player = (await TestFactories.SettleCombatHarness(runner)).Player;
		var scene = runner.Scene();
		PlaceOnOpenFloor(scene, player);
		await runner.SimulateFrames(2, 20);
		var animTree = player.GetNode<AnimationTree>("AnimationTree");
		var playback = (AnimationNodeStateMachinePlayback)animTree.Get("parameters/playback");
		var animPlayer = animTree.GetNode<AnimationPlayer>(animTree.AnimPlayer);
		AssertObject(animPlayer.GetAnimation("Running_A").LoopMode).IsEqual(Animation.LoopModeEnum.Linear);
		float Parameter(string name) => (float)animTree.Get($"parameters/Walking/{name}");

		try
		{
			// Running: the 0.8 s run clip at about 2.8x completes several loops instead of freezing on its last frame.
			Input.ActionPress("move_right");
			int wrapCount = 0;
			float lastPosition = 0f;
			for (int f = 0; f < 60; f++)
			{
				await runner.SimulateFrames(1, 20);
				float position = Parameter("Run/current_position");
				if (position < lastPosition && lastPosition > 0.4f) wrapCount++;
				lastPosition = position;
			}
			AssertBool(player.IsMoving).IsTrue();
			AssertString(playback.GetCurrentNode()).IsEqual("Walking");
			AssertInt(wrapCount).IsGreaterEqual(2);
			AssertFloat(Parameter("TimeScale/scale")).IsBetween(2.0f, 3.5f);
			AssertFloat(player.MovementComponent.GroundSpeed).IsGreater(7.0f);
			AssertObject(player.MovementComponent.LastMotionKind).IsEqual(MovementMotionKind.Locomotion);
			// At full run speed (7.5 m/s) the blend is pure run.
			AssertFloat(Parameter("Blend2/blend_amount")).IsEqualApprox(1.0f, 0.05f);

			// Walking: 40 % of 7.5 m/s is 3.0 m/s, which blends (3.0 - 1.5) / 4.0 = 0.375 toward the run clip.
			Input.ActionPress("walk");
			float HorizontalSpeed() => new Vector2(player.MovementComponent.Velocity.X, player.MovementComponent.Velocity.Z).Length();
			await TestFactories.AwaitPhysicsUntil(runner, () => Mathf.Abs(HorizontalSpeed() - 3.0f) < 0.1f, 1);
			await runner.SimulateFrames(5, 20);
			AssertBool(player.IsMoving).IsTrue();
			AssertString(playback.GetCurrentNode()).IsEqual("Walking");
			AssertFloat(Parameter("Blend2/blend_amount")).IsEqualApprox(0.375f, 0.05f);
			AssertFloat(HorizontalSpeed()).IsEqualApprox(3.0f, 0.1f);
			Input.ActionRelease("walk");
			Input.ActionRelease("move_right");
			await TestFactories.AwaitPhysicsUntil(runner, () => !player.IsMoving, 1);

			// Blocked: walls 1.5 m away on every side (outside the 0.75 m player radius) stop the run, and after the
			// blocked grace (0.15 s) and the crossfade (0.3 s) the tree is idle.
			var wall = new StaticBody3D { Position = player.GlobalPosition, CollisionLayer = 2, CollisionMask = 0 };
			wall.AddChild(new CollisionShape3D { Position = new Vector3(1.5f, 1, 0), Shape = new BoxShape3D { Size = new Vector3(0.5f, 4f, 4f) } });
			wall.AddChild(new CollisionShape3D { Position = new Vector3(-1.5f, 1, 0), Shape = new BoxShape3D { Size = new Vector3(0.5f, 4f, 4f) } });
			wall.AddChild(new CollisionShape3D { Position = new Vector3(0, 1, 1.5f), Shape = new BoxShape3D { Size = new Vector3(4f, 4f, 0.5f) } });
			wall.AddChild(new CollisionShape3D { Position = new Vector3(0, 1, -1.5f), Shape = new BoxShape3D { Size = new Vector3(4f, 4f, 0.5f) } });
			scene.AddChild(wall);
			await runner.SimulateFrames(2, 20);
			Input.ActionPress("move_right");
			await runner.SimulateFrames(35, 20);
			AssertFloat(player.MovementComponent.GroundSpeed).IsLessEqual(0.05f);
			AssertBool(player.IsMoving).IsFalse();
			AssertBool(player.MovementComponent.IsMoving).IsFalse();
			AssertString(playback.GetCurrentNode()).IsEqual("Idle");
		}
		finally
		{
			Input.ActionRelease("move_right");
			Input.ActionRelease("walk");
		}
	}

	/// <summary>Monster run clips loop on the monster's own copy of its animation library.</summary>
	[TestCase]
	public void MonsterLocomotionAnimationsLoop()
	{
		var skeleton = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>(TestFactories.MinionScene).Instantiate<CharacterBody3D>());
		var animTree = skeleton.GetNode<AnimationTree>("EnemyBehaviorComponent/AnimationTree");
		var animPlayer = animTree.GetNode<AnimationPlayer>(animTree.AnimPlayer);
		AssertObject(animPlayer.GetAnimation("Running_A").LoopMode).IsEqual(Animation.LoopModeEnum.Linear);
	}
}
