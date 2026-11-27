namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Plate traps (a pressure plate and the wall launcher it sets off) staged in the combat arena through the
/// debug menu's trap tool, against the real hero.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class PlateTrapTest
{
	private const string DartPlate = "res://scenes/props/traps/triggered/dart_plate.tres";
	private const string HoldMode = "res://scenes/props/triggers/modes/hold.tres";

	/// <summary>The quiet arena with one staged dart trap whose plate sets its launcher off in <paramref name="mode"/>
	/// (the profile's own, Pulse, by default).</summary>
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, PressurePlate Plate, WallLauncher Launcher)> Stage(
		string mode = null)
	{
		var (runner, harness) = await TestFactories.LoadQuietHarness();
		var traps = TestFactories.Tool<TrapTool>(runner.Scene());
		if (mode != null) traps.PlateMode = TestFactories.Load<TriggerMode>(mode);
		var trap = traps.StageTriggered(TestFactories.Load<TriggeredTrapProfile>(DartPlate));
		await runner.AwaitIdleFrame();
		return (runner, harness, trap.GetChildren().OfType<PressurePlate>().Single(), trap.GetChildren().OfType<WallLauncher>().Single());
	}

	/// <summary>Beside the plate, along the hero's path: off it, and out of the launcher's lane.</summary>
	private static Vector3 Beside(PressurePlate plate, WallLauncher launcher) =>
		plate.GlobalPosition + launcher.Shot.FireDirection.Cross(Vector3.Up) * 1.5f;

	/// <summary>Walking past the plate sets nothing off; stepping on it fires one dart that hits the hero.</summary>
	[TestCase]
	public async Task AnUnawareHeroWhoStepsOnThePlateIsHitOnce()
	{
		var (runner, harness, plate, launcher) = await Stage();
		using var scope = runner;
		var hero = harness.Player;
		var outcomes = TestFactories.RecordOutcomes(hero);
		TestFactories.PlaceAt(hero, Beside(plate, launcher));
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertInt(launcher.ShotsFired).IsEqual(0);

		TestFactories.PlaceAt(hero, plate.GlobalPosition);
		await TestFactories.AwaitPhysicsUntil(runner, () => outcomes.Count > 0, timeout: 1);
		await TestFactories.AwaitPhysicsSeconds(runner, launcher.Interval);
		AssertInt(launcher.ShotsFired).IsEqual(1);
		AssertInt(outcomes.Count).IsEqual(1);
		AssertObject(outcomes[0]).IsEqual(DamageOutcome.Damaged);
		AssertBool(plate.IsDiscovered).IsTrue();
	}

	/// <summary>The click is the only warning once the hero is on the plate: a dodge out of the lane on it escapes.</summary>
	[TestCase]
	public async Task DodgingOnTheClickEscapesTheDart()
	{
		var (runner, harness, plate, launcher) = await Stage();
		using var scope = runner;
		var hero = harness.Player;
		var outcomes = TestFactories.RecordOutcomes(hero);
		TestFactories.PlaceAt(hero, plate.GlobalPosition);
		await TestFactories.AwaitPhysicsUntil(runner, () => plate.IsPressed, timeout: 1);
		Vector3 alongPath = launcher.Shot.FireDirection.Cross(Vector3.Up);
		AssertObject(hero.DodgeComponent.TryDodge(alongPath)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsUntil(runner, () => launcher.ShotsFired == 1, timeout: 1);
		await TestFactories.AwaitPhysicsSeconds(runner, .4f);
		AssertThat(outcomes).NotContains(DamageOutcome.Damaged);
	}

	/// <summary>Held on, the launcher keeps firing while the hero stands on the plate, and stops once they step off.</summary>
	[TestCase]
	public async Task AHoldLauncherFiresUntilTheHeroStepsOff()
	{
		var (runner, harness, plate, launcher) = await Stage(HoldMode);
		using var scope = runner;
		TestFactories.PlaceAt(harness.Player, plate.GlobalPosition);
		await TestFactories.AwaitPhysicsUntil(runner, () => launcher.ShotsFired >= 2, timeout: 3);
		TestFactories.PlaceAt(harness.Player, Beside(plate, launcher));
		await TestFactories.AwaitPhysicsUntil(runner, () => !plate.IsPressed, timeout: 1);
		int fired = launcher.ShotsFired;
		await TestFactories.AwaitPhysicsSeconds(runner, launcher.Interval * 1.5f);
		AssertInt(launcher.ShotsFired).IsEqual(fired);
		AssertBool(launcher.IsActive).IsFalse();
	}

	/// <summary>Trap plates answer to the hero only: a monster standing on one sets nothing off.</summary>
	[TestCase]
	public async Task AMonsterOnThePlateSetsNothingOff()
	{
		var (runner, harness, plate, launcher) = await Stage();
		using var scope = runner;
		var monster = TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		TestFactories.PlaceAt(monster, plate.GlobalPosition);
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertInt(launcher.ShotsFired).IsEqual(0);
		AssertBool(plate.IsPressed).IsFalse();
	}
}
