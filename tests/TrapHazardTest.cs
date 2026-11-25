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
	private const string DartSpout = "res://scenes/props/traps/wall_spout_slit.tscn";
	private const string Pendulum = "res://scenes/props/traps/pendulum_blade.tscn";

	/// <summary>The arena with the hero standing still and the practice target away, and one staged gauntlet of <paramref name="scene"/>.</summary>
	private static async Task<(ISceneRunner Runner, Player Hero, TrapGauntlet Gauntlet, T[] Hazards)> Stage<T>(string scene,
		HazardPattern pattern = HazardPattern.Unison, int rows = 1, float period = 1.2f) where T : Node3D
	{
		var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		TestFactories.MovePracticeTargetAway(harness);
		var traps = TestFactories.Tool<TrapTool>(runner.Scene());
		traps.Rows = rows;
		traps.Pattern = pattern;
		traps.Period = period;
		var gauntlet = traps.Stage(TestFactories.Load<PackedScene>(scene));
		return (runner, harness.Player, gauntlet, gauntlet.GetChildren().OfType<T>().ToArray());
	}

	/// <summary>Puts the hero <paramref name="distance"/> metres down <paramref name="spout"/>'s lane.</summary>
	private static void StandInLane(Player hero, WallSpout spout, float distance)
	{
		Vector3 point = spout.Muzzle.GlobalPosition + spout.FireDirection * distance;
		hero.GlobalPosition = point with { Y = hero.GlobalPosition.Y };
		hero.ResetPhysicsInterpolation();
	}

	/// <summary>Puts the hero on the path under <paramref name="blade"/>, <paramref name="along"/> metres back from its swing.</summary>
	private static void StandAtPendulum(Player hero, PendulumBlade blade, float along)
	{
		Vector3 point = blade.GlobalPosition + blade.GlobalBasis.Z * along;
		hero.GlobalPosition = point with { Y = hero.GlobalPosition.Y };
		hero.ResetPhysicsInterpolation();
	}

	private static List<DamageOutcome> RecordOutcomes(Player hero)
	{
		var outcomes = new List<DamageOutcome>();
		hero.HurtBoxComponent.DamageResolved += (outcome, _, _) => outcomes.Add((DamageOutcome)outcome);
		return outcomes;
	}

	[TestCase]
	public async Task ADartHitsTheHeroInItsLaneOncePerShot()
	{
		var (runner, hero, _, spouts) = await Stage<WallSpout>(DartSpout);
		using var scope = runner;
		StandInLane(hero, spouts[0], 3.5f);
		var outcomes = RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsUntil(runner, () => spouts[0].ShotsFired >= 2);
		await TestFactories.AwaitPhysicsSeconds(runner, .8f);
		AssertInt(outcomes.Count(outcome => outcome == DamageOutcome.Damaged)).IsEqual(2);
	}

	[TestCase]
	public async Task DodgingIntoADartPassesThroughIt()
	{
		var (runner, hero, _, spouts) = await Stage<WallSpout>(DartSpout);
		using var scope = runner;
		var spout = spouts[0];
		StandInLane(hero, spout, 5f);
		var outcomes = RecordOutcomes(hero);
		Node3D shot = null;
		spout.Fired += fired => shot = fired;
		Vector3 chest = hero.GlobalPosition + Vector3.Up;
		await TestFactories.AwaitPhysicsUntil(runner, () => GodotObject.IsInstanceValid(shot) && shot.GlobalPosition.DistanceTo(chest) < 2.5f);
		AssertObject(hero.DodgeComponent.TryDodge(-spout.FireDirection)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsSeconds(runner, .5f);
		AssertThat(outcomes).Contains(DamageOutcome.Dodged);
		AssertThat(outcomes).NotContains(DamageOutcome.Damaged);
	}

	[TestCase]
	public async Task AMonsterInTheLaneTakesNoDamageButStopsTheDart()
	{
		var (runner, hero, _, spouts) = await Stage<WallSpout>(DartSpout);
		using var scope = runner;
		var spout = spouts[0];
		StandInLane(hero, spout, 5f);
		var harness = runner.Scene().GetNode<CombatHarness>("Instructions");
		var monster = TestFactories.SpawnHarnessMonster(harness, TestFactories.MinionScene);
		Vector3 between = spout.Muzzle.GlobalPosition + spout.FireDirection * 2.5f;
		monster.GlobalPosition = between with { Y = monster.GlobalPosition.Y };
		monster.ResetPhysicsInterpolation();
		float health = monster.HealthComponent.CurrentHealth;
		var outcomes = RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsUntil(runner, () => spout.ShotsFired >= 1);
		await TestFactories.AwaitPhysicsSeconds(runner, .8f);
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
		var (runner, hero, gauntlet, spouts) = await Stage<WallSpout>(DartSpout);
		using var scope = runner;
		var spout = spouts[0];
		StandInLane(hero, spout, 3.5f);
		hero.GlobalPosition += Vector3.Back * 3;
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

	/// <summary>A wave lags each row by the hero's walk from the first, so a walker meets every row at the same beat.</summary>
	[TestCase]
	public async Task AWaveFiresEachRowAsLateAsTheHeroTakesToWalkThere()
	{
		var (runner, hero, gauntlet, spouts) = await Stage<WallSpout>(DartSpout, HazardPattern.Wave, rows: 3, period: 2.4f);
		using var scope = runner;
		var firstShot = new Dictionary<WallSpout, double>();
		foreach (var spout in spouts) spout.Fired += _ => firstShot.TryAdd(spout, gauntlet.Clock);
		await TestFactories.AwaitPhysicsUntil(runner, () => firstShot.Count == spouts.Length, 6);
		Vector3 path = (hero.MovementComponent.LookAtDirection with { Y = 0 }).Normalized();
		foreach (var spout in spouts.Skip(1))
		{
			double walk = (spout.GlobalPosition - spouts[0].GlobalPosition).Dot(path) / hero.Stats.Speed;
			double lag = Mathf.PosMod(firstShot[spout] - firstShot[spouts[0]] - walk, spout.Period);
			AssertFloat((float)Mathf.Min(lag, spout.Period - lag)).IsLess(.05f);
		}
	}

	/// <summary>Beside its swing a hero is never cut; under it, once per swing, and pushed along the swing.</summary>
	[TestCase]
	public async Task APendulumCutsAHeroUnderItOncePerSwingAndPushesThemAlongIt()
	{
		var (runner, hero, _, blades) = await Stage<PendulumBlade>(Pendulum, period: 2f);
		using var scope = runner;
		var blade = blades[0];
		StandAtPendulum(hero, blade, 2f);
		var outcomes = RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsSeconds(runner, 2.1f);
		AssertThat(outcomes).IsEmpty();

		StandAtPendulum(hero, blade, 0);
		await TestFactories.AwaitPhysicsUntil(runner, () => outcomes.Count > 0, 2);
		Vector3 struck = hero.GlobalPosition;
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);
		AssertThat(outcomes).ContainsExactly(DamageOutcome.Damaged);
		AssertFloat(Mathf.Abs((hero.GlobalPosition - struck).Dot(blade.GlobalBasis.X))).IsGreater(.3f);
	}

	[TestCase]
	public async Task DodgingThroughASwingingBladePassesThroughIt()
	{
		var (runner, hero, _, blades) = await Stage<PendulumBlade>(Pendulum, period: 2f);
		using var scope = runner;
		var blade = blades[0];
		StandAtPendulum(hero, blade, 1.5f);
		var outcomes = RecordOutcomes(hero);
		await TestFactories.AwaitPhysicsUntil(runner, () => Mathf.Abs(blade.Arm.Rotation.Z) < Mathf.DegToRad(3), 3);
		AssertObject(hero.DodgeComponent.TryDodge(-blade.GlobalBasis.Z)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsSeconds(runner, .4f);
		AssertThat(outcomes).Contains(DamageOutcome.Dodged);
		AssertThat(outcomes).NotContains(DamageOutcome.Damaged);
	}
}
