namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Wall spouts and pendulums staged in the combat arena through the debug menu's trap tool, against the real hero.</summary>
[TestSuite]
[RequireGodotRuntime]
public partial class TrapHazardTest
{
	private const string DartSlit = "res://scenes/levels/rooms/props/traps/dart_slit.tres";
	private const string Pendulum = "res://scenes/levels/rooms/props/traps/pendulum_blade.tres";

	/// <summary>The quiet arena with one staged gauntlet of the <paramref name="hazard"/> prop.</summary>
	private static async Task<(ISceneRunner Runner, CombatHarness Harness, TrapGauntlet Gauntlet, T[] Hazards)> Stage<T>(string hazard,
		HazardPattern pattern = HazardPattern.Unison, int rows = 1, float period = 1.2f) where T : TrapHazard
	{
		var (runner, harness) = await TestFactories.LoadQuietHarness();
		var traps = TestFactories.Tool<TrapTool>(runner.Scene());
		traps.Rows = rows;
		traps.Pattern = pattern;
		traps.Period = period;
		var gauntlet = traps.Stage(TestFactories.Load<Prop>(hazard));
		return (runner, harness, gauntlet, gauntlet.GetChildren().OfType<T>().ToArray());
	}

	/// <summary>Stands <paramref name="body"/> at <paramref name="point"/>'s spot on the ground, keeping its own height.</summary>
	private static void PlaceAt(Node3D body, Vector3 point)
	{
		body.GlobalPosition = point with { Y = body.GlobalPosition.Y };
		body.ResetPhysicsInterpolation();
	}

	/// <summary>The point <paramref name="distance"/> metres down <paramref name="spout"/>'s lane.</summary>
	private static Vector3 Lane(WallSpout spout, float distance) => spout.Muzzle.GlobalPosition + spout.FireDirection * distance;

	private static List<DamageOutcome> RecordOutcomes(Player hero)
	{
		var outcomes = new List<DamageOutcome>();
		hero.HurtBoxComponent.DamageResolved += (outcome, _, _) => outcomes.Add((DamageOutcome)outcome);
		return outcomes;
	}

	/// <summary>Waits until <paramref name="spout"/> has fired <paramref name="shots"/> shots and the last one has hit
	/// something or flown out.</summary>
	private static Task AwaitShotResolved(ISceneRunner runner, WallSpout spout, int shots)
	{
		Node3D last = null;
		spout.Fired += shot => last = shot;
		return TestFactories.AwaitPhysicsUntil(runner, () => spout.ShotsFired >= shots && !last.IsPhysicsProcessing());
	}

	[TestCase]
	public async Task ADartHitsTheHeroInItsLaneOncePerShot()
	{
		var (runner, harness, _, spouts) = await Stage<WallSpout>(DartSlit);
		using var scope = runner;
		PlaceAt(harness.Player, Lane(spouts[0], 3.5f));
		var outcomes = RecordOutcomes(harness.Player);
		await AwaitShotResolved(runner, spouts[0], 2);
		AssertInt(outcomes.Count(outcome => outcome == DamageOutcome.Damaged)).IsEqual(2);
	}

	[TestCase]
	public async Task DodgingIntoADartPassesThroughIt()
	{
		var (runner, harness, _, spouts) = await Stage<WallSpout>(DartSlit);
		using var scope = runner;
		var hero = harness.Player;
		var spout = spouts[0];
		PlaceAt(hero, Lane(spout, 5f));
		var outcomes = RecordOutcomes(hero);
		Node3D shot = null;
		spout.Fired += fired => shot = fired;
		Vector3 chest = hero.GlobalPosition + Vector3.Up;
		await TestFactories.AwaitPhysicsUntil(runner, () => GodotObject.IsInstanceValid(shot) && shot.GlobalPosition.DistanceTo(chest) < 2.5f);
		AssertObject(hero.DodgeComponent.TryDodge(-spout.FireDirection)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsSeconds(runner, .4f);
		AssertThat(outcomes).Contains(DamageOutcome.Dodged);
		AssertThat(outcomes).NotContains(DamageOutcome.Damaged);
	}

	[TestCase]
	public async Task AMonsterInTheLaneTakesNoDamageButStopsTheDart()
	{
		var (runner, harness, _, spouts) = await Stage<WallSpout>(DartSlit);
		using var scope = runner;
		var spout = spouts[0];
		PlaceAt(harness.Player, Lane(spout, 5f));
		var monster = TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		PlaceAt(monster, Lane(spout, 2.5f));
		float health = monster.HealthComponent.CurrentHealth;
		var outcomes = RecordOutcomes(harness.Player);
		await AwaitShotResolved(runner, spout, 1);
		AssertFloat(monster.HealthComponent.CurrentHealth).IsEqual(health);
		AssertThat(outcomes).IsEmpty();
	}

	/// <summary>
	/// Asleep, a gauntlet never fires; woken during a warning, it skips that shot, so every shot it fires was announced
	/// by a full warning.
	/// </summary>
	[TestCase]
	public async Task AGauntletFiresOnlyWhileTheHeroIsNearAndOnlyAfterAFullWarning()
	{
		var (runner, harness, gauntlet, spouts) = await Stage<WallSpout>(DartSlit);
		using var scope = runner;
		var spout = spouts[0];
		PlaceAt(harness.Player, Lane(spout, 3.5f) + Vector3.Back * 3);
		gauntlet.ActivationRadius = 1;
		await TestFactories.AwaitPhysicsSeconds(runner, 1.5f);
		AssertBool(gauntlet.IsAwake).IsFalse();
		AssertInt(spout.ShotsFired).IsEqual(0);

		float UntilShot() => (float)((1 - Mathf.PosMod(gauntlet.Clock / spout.Period + spout.Phase, 1)) * spout.Period);
		await TestFactories.AwaitPhysicsUntil(runner, () => UntilShot() is > .15f and < .4f);
		gauntlet.ActivationRadius = 16;
		await TestFactories.AwaitPhysicsSeconds(runner, .45f);
		AssertBool(gauntlet.IsAwake).IsTrue();
		AssertInt(spout.ShotsFired).IsEqual(0);

		bool warned = false;
		await TestFactories.AwaitPhysicsUntil(runner, () => (warned |= spout.IsWindingUp) && spout.ShotsFired == 1);
		AssertBool(warned).IsTrue();
	}

	/// <summary>A wave sweeps along the gauntlet: each row fires a quarter beat after the row before it.</summary>
	[TestCase]
	public async Task AWaveFiresEachRowAQuarterBeatAfterTheRowBefore()
	{
		var (runner, _, gauntlet, spouts) = await Stage<WallSpout>(DartSlit, HazardPattern.Wave, rows: 3);
		using var scope = runner;
		var firstShot = new Dictionary<WallSpout, double>();
		foreach (var spout in spouts) spout.Fired += _ => firstShot.TryAdd(spout, gauntlet.Clock);
		await TestFactories.AwaitPhysicsUntil(runner, () => firstShot.Count == spouts.Length, 3);
		foreach (var (before, after) in spouts.Zip(spouts.Skip(1)))
		{
			double lag = Mathf.PosMod(firstShot[after] - firstShot[before], after.Period);
			AssertFloat((float)lag).IsEqualApprox(after.Period / 4, .05f);
		}
	}

	/// <summary>Beside its swing a hero is never cut; under it, once per swing, and pushed along the swing.</summary>
	[TestCase]
	public async Task APendulumCutsAHeroUnderItOncePerSwingAndPushesThemAlongIt()
	{
		var (runner, harness, _, blades) = await Stage<PendulumBlade>(Pendulum, period: 1.6f);
		using var scope = runner;
		var hero = harness.Player;
		var blade = blades[0];
		PlaceAt(hero, blade.GlobalPosition + blade.GlobalBasis.Z * 2f);
		var outcomes = RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsSeconds(runner, 1.7f);
		AssertThat(outcomes).IsEmpty();

		// Step under it as it crosses the middle: a quarter period (0.4 s) of this swing remains, so a second cut can only
		// come from the next one.
		await TestFactories.AwaitPhysicsUntil(runner, () => Mathf.Abs(blade.Arm.Rotation.Z) < Mathf.DegToRad(2), 2);
		PlaceAt(hero, blade.GlobalPosition);
		await TestFactories.AwaitPhysicsUntil(runner, () => outcomes.Count > 0, 1);
		Vector3 struck = hero.GlobalPosition;
		await TestFactories.AwaitPhysicsSeconds(runner, .25f);
		AssertThat(outcomes).ContainsExactly(DamageOutcome.Damaged);
		AssertFloat(Mathf.Abs((hero.GlobalPosition - struck).Dot(blade.GlobalBasis.X))).IsGreater(.3f);
	}

	[TestCase]
	public async Task DodgingThroughASwingingBladePassesThroughIt()
	{
		var (runner, harness, _, blades) = await Stage<PendulumBlade>(Pendulum, period: 1.6f);
		using var scope = runner;
		var hero = harness.Player;
		var blade = blades[0];
		PlaceAt(hero, blade.GlobalPosition + blade.GlobalBasis.Z * 1.5f);
		var outcomes = RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsUntil(runner, () => Mathf.Abs(blade.Arm.Rotation.Z) < Mathf.DegToRad(3), 3);
		AssertObject(hero.DodgeComponent.TryDodge(-blade.GlobalBasis.Z)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsSeconds(runner, .4f);
		AssertThat(outcomes).Contains(DamageOutcome.Dodged);
		AssertThat(outcomes).NotContains(DamageOutcome.Damaged);
	}
}
