namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Shots fly at chest height so only geometry crossing that line blocks them, while their damage lane still reaches
/// monsters shorter than the flight line.
/// </summary>
[TestSuite, RequireGodotRuntime]
public partial class ProjectileGroundReachTest
{
	private const uint PropsLayer = 1 << 4;

	/// <summary>
	/// Two lanes, one bolt each: with ground reach a bolt clears a low table and still hits the rat behind it; without
	/// ground reach the same bolt passes over a rat in the open.
	/// </summary>
	[TestCase]
	public async Task ChestHeightBoltHitsARatBelowItsFlightLineOverALowTable()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var root = runner.Scene();
		const float otherLane = -6;
		var reached = TestFactories.SpawnCombatMonster(root, new Vector3(6, 0, 0), "ReachedRat", TestFactories.RatScene);
		var overflown = TestFactories.SpawnCombatMonster(root, new Vector3(6, 0, otherLane), "OverflownRat", TestFactories.RatScene);
		var table = new StaticBody3D { Position = new Vector3(4, .4f, 0), CollisionLayer = PropsLayer };
		table.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, .8f, 2) } });
		root.AddChild(table);
		await runner.SimulateFrames(5, 20);
		TestFactories.PrepareCombatMonster(reached);
		TestFactories.PrepareCombatMonster(overflown);

		var scene = TestFactories.Load<PackedScene>("res://scenes/attacks/projectile.tscn");
		foreach (var (lane, groundReach) in new[] { (0f, 1.5f), (otherLane, 0f) })
		{
			var bolt = scene.Instantiate<Projectile>();
			root.AddChild(bolt);
			bolt.GroundReach = groundReach;
			bolt.Initialize(new Vector3(2, 1.2f, lane), Vector3.Right, 16, 10, new AttackStats(5, 5, 0), harness.Player, AttackDefenseTraits.None);
		}
		// Each bolt covers the 4 m to its rat in a quarter second.
		await TestFactories.AwaitPhysicsUntil(runner, () => reached.HealthComponent.CurrentHealth < 50, 1);
		await TestFactories.AwaitPhysicsSeconds(runner, .3f);

		// Side and sneak-attack multipliers vary the amount; only whether each rat was hit matters here.
		AssertFloat(reached.HealthComponent.CurrentHealth).IsLess(50);
		AssertFloat(overflown.HealthComponent.CurrentHealth).IsEqual(50);
	}
}
