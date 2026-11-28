namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Plate traps (a pressure plate and the wall launcher it sets off) staged in the combat arena, against the real
/// hero.</summary>
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
		var (plate, launcher) = TrapStaging.PlateTrap(harness, TestFactories.Load<TriggeredTrapProfile>(DartPlate),
			mode == null ? null : TestFactories.Load<TriggerMode>(mode));
		await runner.AwaitIdleFrame();
		return (runner, harness, plate, launcher);
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

	/// <summary>
	/// A dodge carries the hero over the plate without touching it, so dashing across sets nothing off; a dodge too short
	/// to clear it (an overloaded hero's) ends on the plate, and that counts as stepping on.
	/// </summary>
	[TestCase]
	public async Task DashingOverThePlateSetsNothingOffButLandingOnItDoes()
	{
		var (runner, harness, plate, launcher) = await Stage();
		using var scope = runner;
		var hero = harness.Player;
		Vector3 across = launcher.Shot.FireDirection.Cross(Vector3.Up);
		TestFactories.PlaceAt(hero, plate.GlobalPosition - across * 3);
		AssertObject(hero.DodgeComponent.TryDodge(across)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsUntil(runner, () => !hero.DodgeComponent.IsDodging, timeout: 1);
		await TestFactories.AwaitPhysicsSeconds(runner, .2f);
		AssertFloat(hero.GlobalPosition.HorizontalDistanceTo(plate.GlobalPosition)).IsGreater(2);
		AssertInt(launcher.ShotsFired).IsEqual(0);

		hero.DodgeComponent.DistanceScale = .3f;
		TestFactories.PlaceAt(hero, plate.GlobalPosition + across * 2);
		await TestFactories.AwaitPhysicsUntil(runner, () => hero.DodgeComponent.CooldownRemaining <= 0, timeout: 2);
		AssertObject(hero.DodgeComponent.TryDodge(-across)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsUntil(runner, () => launcher.ShotsFired == 1, timeout: 1);
		AssertBool(plate.IsPressed).IsTrue();
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

	/// <summary>
	/// Held on, the launcher keeps firing while the hero stands on the plate, and stops once they step off. The plate sinks
	/// under the hero and rises again after.
	/// </summary>
	[TestCase]
	public async Task AHoldLauncherFiresUntilTheHeroStepsOff()
	{
		var (runner, harness, plate, launcher) = await Stage(HoldMode);
		using var scope = runner;
		// The slab that moves, inside its fixed frame.
		var slab = plate.GetChildren().OfType<PlatePressComponent>().Single().Target;
		float raised = slab.GlobalPosition.Y;
		TestFactories.PlaceAt(harness.Player, plate.GlobalPosition);
		await TestFactories.AwaitPhysicsUntil(runner, () => launcher.ShotsFired >= 2, timeout: 3);
		AssertFloat(slab.GlobalPosition.Y).IsLess(raised - .05f);
		TestFactories.PlaceAt(harness.Player, Beside(plate, launcher));
		await TestFactories.AwaitPhysicsUntil(runner, () => !plate.IsPressed, timeout: 1);
		int fired = launcher.ShotsFired;
		await TestFactories.AwaitPhysicsSeconds(runner, launcher.Interval * 1.5f);
		AssertInt(launcher.ShotsFired).IsEqual(fired);
		AssertBool(launcher.IsActive).IsFalse();
		AssertFloat(slab.GlobalPosition.Y).IsEqualApprox(raised, .001f);
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
