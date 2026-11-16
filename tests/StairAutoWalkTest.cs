namespace RogueGauntlet.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Regression tests for staircase spawning and arrival/load behavior:
/// 1) Entering a floor on <c>stairs_up.tscn</c> walks forward down the stairs toward Landing in a single smooth walk without triggering <c>UpTransitionTrigger</c>.
/// 2) Restoring an arrival save at the spawn point inside <c>UpTransitionTrigger</c> leaves the player completely still with no <see cref="StairAutoWalk"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class StairAutoWalkTest
{
	[TestCase]
	public async Task EntranceStairsWalkPlayerOnceTowardLandingAndLoadKeepsPlayerStill()
	{
		using var saves = new ScratchSaves();
		GameSession.Instance.BeginRun(1, "barbarian", 4242);

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		var main = (Main)runner.Scene();
		var level = main.GetNode<Level>("Level");
		await runner.SimulateFrames(2, 20);

		Player entered = level.GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		AssertObject(entered).IsNotNull();
		var spawnPoint = level.FindChildren("*", "", true, false).OfType<PlayerSpawnPoint>()
			.First(p => p.SpawnAfterTravelDirection == GameSession.LevelTravelDirection.Down);

		Vector3 startPos = spawnPoint.GlobalPosition;
		Vector3 landingPos = spawnPoint.LandingPosition;
		float initialDistanceToLanding = startPos.HorizontalDistanceTo(landingPos);

		// 1. Fresh entry: single StairAutoWalk moving toward Landing, never back into UpTransitionTrigger.
		AssertBool(StairAutoWalk.IsActive(entered)).IsTrue();
		AssertInt(entered.GetChildren().OfType<StairAutoWalk>().Count()).IsEqual(1);

		for (int i = 0; i < 6; i++)
		{
			await runner.SimulateFrames(1, 20);
		}
		AssertFloat(entered.GlobalPosition.HorizontalDistanceTo(landingPos)).IsLess(initialDistanceToLanding);
		AssertInt(entered.GetChildren().OfType<StairAutoWalk>().Count()).IsEqual(1);

		// Finish the entrance walk and verify total displacement is only ~AutoWalkDistance (~3.4m), not a second 5.6m walk.
		for (int i = 0; i < 60 && StairAutoWalk.IsActive(entered); i++)
		{
			await runner.SimulateFrames(1, 20);
		}
		AssertBool(StairAutoWalk.IsActive(entered)).IsFalse();
		float walkedDist = entered.GlobalPosition.HorizontalDistanceTo(startPos);
		AssertFloat(walkedDist).IsBetween(2.5f, 4.3f);

		// Wait past the 1s stair-arrival lock window to confirm no second forward walk ever starts.
		Vector3 settledPos = entered.GlobalPosition;
		for (int i = 0; i < 20; i++)
		{
			await runner.SimulateFrames(1, 20);
		}
		AssertBool(StairAutoWalk.IsActive(entered)).IsFalse();
		AssertFloat(entered.GlobalPosition.HorizontalDistanceTo(settledPos)).IsLess(0.05f);

		// 2. Simulating LoadGame from the arrival save: restore the player at the spawn point inside UpTransitionTrigger.
		SaveGame arrivalSave = SaveService.Load(1);
		AssertObject(arrivalSave?.Player).IsNotNull();
		PlayerSaveCodec.Apply(entered, arrivalSave.Player);
		SignalBus.EmitPlayerSpawned(entered);

		// Allow one physics step for Jolt stair-step capsule depenetration, then verify the hero remains completely stationary.
		await runner.SimulateFrames(2, 20);
		Vector3 settledLoadPos = entered.GlobalPosition;
		for (int i = 0; i < 15; i++)
		{
			await runner.SimulateFrames(1, 20);
		}

		AssertBool(StairAutoWalk.IsActive(entered)).IsFalse();
		AssertFloat(entered.GlobalPosition.HorizontalDistanceTo(startPos)).IsLess(0.45f);
		AssertFloat(entered.GlobalPosition.HorizontalDistanceTo(settledLoadPos)).IsLess(0.02f);

		// 3. Leaving the stairs clears the spawn block so walking back onto UpTransitionTrigger works normally.
		entered.GlobalPosition = landingPos;
		await runner.SimulateFrames(4, 20);
		AssertBool(StairAutoWalk.IsActive(entered)).IsFalse();

		entered.GlobalPosition = startPos;
		await runner.SimulateFrames(4, 20);
		AssertBool(StairAutoWalk.IsActive(entered)).IsTrue();
	}

	[TestCase]
	public async Task DodgingIntoDescentTriggerChangesFloorWithoutReentering()
	{
		using var saves = new ScratchSaves();
		GameSession.Instance.BeginRun(1, "barbarian", 4242);

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		var main = (Main)runner.Scene();
		var level = main.GetNode<Level>("Level");
		await runner.SimulateFrames(2, 20);

		Player player = level.GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		AssertObject(player).IsNotNull();
		StairAutoWalk.Cancel(player);
		await WaitUntilTransitionUnlocked();

		LevelTransitionTrigger descent = level.Floor.Descent;
		AssertObject(descent).IsNotNull();
		Marker3D landing = descent.GetParent().GetNode<Marker3D>("Landing");
		Vector3 dir = (descent.GlobalPosition - landing.GlobalPosition) with { Y = 0f };
		dir = dir.Normalized();

		// Stand on the stair landing just outside the descent trigger, then dodge straight into it.
		player.GlobalPosition = landing.GlobalPosition + dir * 3.8f;
		player.ResetPhysicsInterpolation();
		await runner.SimulateFrames(3, 20);
		AssertInt((int)GameSession.Instance.ActiveDungeonDepth).IsEqual(1);

		DefenseRequestResult dodgeResult = player.DodgeComponent.TryDodge(dir);
		AssertInt((int)dodgeResult).IsEqual((int)DefenseRequestResult.Success);
		AssertBool(player.DodgeComponent.IsDodging).IsTrue();

		for (int i = 0; i < 30 && GameSession.Instance.ActiveDungeonDepth == 1; i++)
		{
			await runner.SimulateFrames(1, 20);
		}

		AssertInt((int)GameSession.Instance.ActiveDungeonDepth).IsEqual(2);
	}

	[TestCase]
	public async Task EnteringDescentTriggerDuringItemUseRetriesOnceActionFinishes()
	{
		using var saves = new ScratchSaves();
		GameSession.Instance.BeginRun(1, "barbarian", 4242);

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		var main = (Main)runner.Scene();
		var level = main.GetNode<Level>("Level");
		await runner.SimulateFrames(2, 20);

		Player player = level.GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		AssertObject(player).IsNotNull();
		StairAutoWalk.Cancel(player);
		await WaitUntilTransitionUnlocked();

		ActorActionRequestResult acquired = player.ActionGate.TryAcquire(
			ActorActionKind.ItemUse,
			ActorActionPhase.Active,
			_ => { },
			out ActorActionLease lease);
		AssertInt((int)acquired).IsEqual((int)ActorActionRequestResult.Success);

		// Move directly into the descent trigger while ItemUse is active: entry refuses without leaving the trigger.
		player.GlobalPosition = level.Floor.Descent.GlobalPosition;
		player.ResetPhysicsInterpolation();
		await runner.SimulateFrames(3, 20);
		AssertInt((int)GameSession.Instance.ActiveDungeonDepth).IsEqual(1);

		// Completing the item action inside the trigger activates transition without walking out and back in.
		player.ActionGate.Release(lease);
		for (int i = 0; i < 10 && GameSession.Instance.ActiveDungeonDepth == 1; i++)
		{
			await runner.SimulateFrames(1, 20);
		}

		AssertInt((int)GameSession.Instance.ActiveDungeonDepth).IsEqual(2);
	}

	private static async Task WaitUntilTransitionUnlocked()
	{
		for (int i = 0; i < 30 && !GameSession.Instance.CanUseTransition(GameSession.LevelTravelDirection.Down); i++)
		{
			await Task.Delay(50);
		}
	}

	private sealed class ScratchSaves : IDisposable
	{
		private readonly string _previous = SaveService.SaveDirectory;
		private readonly string _directory = $"user://test-stair-saves-{Guid.NewGuid():N}";

		public ScratchSaves() => SaveService.SaveDirectory = _directory;

		public void Dispose()
		{
			GameSession.Instance?.ClearSession();
			LoadingScreen.Instance?.Dismiss();
			GameLog.ResetToDefaults();
			((SceneTree)Engine.GetMainLoop()).Paused = false;
			SaveService.SaveDirectory = _previous;
			string path = ProjectSettings.GlobalizePath(_directory);
			if (Directory.Exists(path)) Directory.Delete(path, true);
		}
	}
}