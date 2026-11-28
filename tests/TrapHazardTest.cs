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

	/// <summary>The point <paramref name="distance"/> metres down <paramref name="spout"/>'s lane.</summary>
	private static Vector3 Lane(WallSpout spout, float distance) => spout.Shot.Muzzle.GlobalPosition + spout.Shot.FireDirection * distance;

	/// <summary>Waits until <paramref name="spout"/> has fired <paramref name="shots"/> shots and the last one has hit
	/// something or flown out.</summary>
	private static Task AwaitShotResolved(ISceneRunner runner, WallSpout spout, int shots)
	{
		Node3D last = null;
		spout.Fired += shot => last = shot;
		return TestFactories.AwaitPhysicsUntil(runner, () => spout.ShotsFired >= shots && !last.IsPhysicsProcessing());
	}

	[TestCase]
	public async Task DodgingIntoADartPassesThroughIt()
	{
		var (runner, harness, _, spouts) = await Stage<WallSpout>(DartSlit);
		using var scope = runner;
		var hero = harness.Player;
		var spout = spouts[0];
		TestFactories.PlaceAt(hero, Lane(spout, 5f));
		var outcomes = TestFactories.RecordOutcomes(hero);
		Node3D shot = null;
		spout.Fired += fired => shot = fired;
		Vector3 chest = hero.GlobalPosition + Vector3.Up;
		await TestFactories.AwaitPhysicsUntil(runner, () => GodotObject.IsInstanceValid(shot) && shot.GlobalPosition.DistanceTo(chest) < 2.5f);
		AssertObject(hero.DodgeComponent.TryDodge(-spout.Shot.FireDirection)).IsEqual(DefenseRequestResult.Success);
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
		TestFactories.PlaceAt(harness.Player, Lane(spout, 5f));
		var monster = TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		TestFactories.PlaceAt(monster, Lane(spout, 2.5f));
		float health = monster.HealthComponent.CurrentHealth;
		var outcomes = TestFactories.RecordOutcomes(harness.Player);
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
		TestFactories.PlaceAt(harness.Player, Lane(spout, 3.5f) + Vector3.Back * 3);
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

	/// <summary>
	/// A wave sweeps along the gauntlet: each row fires a quarter beat after the row before it, and a dart hits a hero
	/// standing in its lane once per shot.
	/// </summary>
	[TestCase]
	public async Task AWaveFiresEachRowAQuarterBeatAfterTheRowBeforeAndEachDartHitsOnce()
	{
		var (runner, harness, gauntlet, spouts) = await Stage<WallSpout>(DartSlit, HazardPattern.Wave, rows: 3);
		using var scope = runner;
		TestFactories.PlaceAt(harness.Player, Lane(spouts[0], 3.5f));
		var outcomes = TestFactories.RecordOutcomes(harness.Player);
		var firstShot = new Dictionary<WallSpout, double>();
		foreach (var spout in spouts) spout.Fired += _ => firstShot.TryAdd(spout, gauntlet.Clock);
		await TestFactories.AwaitPhysicsUntil(runner, () => firstShot.Count == spouts.Length, 3);
		foreach (var (before, after) in spouts.Zip(spouts.Skip(1)))
		{
			double lag = Mathf.PosMod(firstShot[after] - firstShot[before], after.Period);
			AssertFloat((float)lag).IsEqualApprox(after.Period / 4, .05f);
		}
		await AwaitShotResolved(runner, spouts[0], 2);
		AssertInt(outcomes.Count(outcome => outcome == DamageOutcome.Damaged)).IsEqual(2);
	}

	/// <summary>
	/// Beside its swing a hero is never cut; under it, cut once and pushed out of the blade's path, clear of its reach, so
	/// later swings miss.
	/// </summary>
	[TestCase]
	public async Task APendulumCutsAHeroUnderItOnceAndPushesThemOutOfItsPath()
	{
		var (runner, harness, _, blades) = await Stage<PendulumBlade>(Pendulum, period: 1.6f);
		using var scope = runner;
		var hero = harness.Player;
		var blade = blades[0];
		Vector3 across = blade.GlobalBasis.Z;
		TestFactories.PlaceAt(hero, blade.GlobalPosition + across * 2f);
		var outcomes = TestFactories.RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsSeconds(runner, 1.7f);
		AssertThat(outcomes).IsEmpty();

		// Step just off the middle of its path as it swings through: the cut pushes back to that side.
		await TestFactories.AwaitPhysicsUntil(runner, () => Mathf.Abs(blade.Arm.Rotation.Z) < Mathf.DegToRad(2), 2);
		TestFactories.PlaceAt(hero, blade.GlobalPosition - across * .2f);
		await TestFactories.AwaitPhysicsUntil(runner, () => outcomes.Count > 0, 1);
		await TestFactories.AwaitPhysicsUntil(runner, () => (hero.GlobalPosition - blade.GlobalPosition).Dot(across) < -blade.Clearance + .1f, 1.5f);
		// The blade sweeps its path every half period: one more pass misses the hero it pushed out.
		await TestFactories.AwaitPhysicsSeconds(runner, 1f);
		AssertThat(outcomes).ContainsExactly(DamageOutcome.Damaged);
	}

	[TestCase]
	public async Task DodgingThroughASwingingBladePassesThroughIt()
	{
		var (runner, harness, _, blades) = await Stage<PendulumBlade>(Pendulum, period: 1.6f);
		using var scope = runner;
		var hero = harness.Player;
		var blade = blades[0];
		TestFactories.PlaceAt(hero, blade.GlobalPosition + blade.GlobalBasis.Z * 1.5f);
		var outcomes = TestFactories.RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsUntil(runner, () => Mathf.Abs(blade.Arm.Rotation.Z) < Mathf.DegToRad(3), 3);
		AssertObject(hero.DodgeComponent.TryDodge(-blade.GlobalBasis.Z)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsSeconds(runner, .4f);
		AssertThat(outcomes).Contains(DamageOutcome.Dodged);
		AssertThat(outcomes).NotContains(DamageOutcome.Damaged);
	}
}
