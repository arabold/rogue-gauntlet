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

	[TestCase(1.5f, false, true)]
	[TestCase(1.5f, true, true)]
	[TestCase(0f, false, false)]
	public async Task ChestHeightBoltHitsARatBelowItsFlightLine(float groundReach, bool lowTableInTheWay, bool expectHit)
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 5);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.MovementComponent.SetPhysicsProcess(false);
		var root = runner.Scene();
		var rat = TestFactories.SpawnCombatMonster(root, new Vector3(6, 0, 0), "Rat", "res://scenes/enemies/rat/rat.tscn");
		if (lowTableInTheWay)
		{
			var table = new StaticBody3D { Position = new Vector3(4, .4f, 0), CollisionLayer = PropsLayer };
			table.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, .8f, 2) } });
			root.AddChild(table);
		}
		await runner.SimulateFrames(5);
		TestFactories.PrepareCombatMonster(rat);

		var bolt = GD.Load<PackedScene>("res://scenes/attacks/projectile.tscn").Instantiate<Projectile>();
		root.AddChild(bolt);
		bolt.GroundReach = groundReach;
		bolt.Initialize(new Vector3(2, 1.2f, 0), Vector3.Right, 16, 10, new AttackStats(5, 5, 0), harness.Player, AttackDefenseTraits.None);
		var tree = root.GetTree();
		for (int i = 0; i < 40; i++) await root.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

		// Side and sneak-attack multipliers vary the amount; only whether the rat was hit matters here.
		AssertBool(rat.HealthComponent.CurrentHealth < 50).IsEqual(expectHit);
	}
}
