namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The Prison's orc tiers beside the grunt: the spearman throws javelins from his hand, and the shaman hastes the orcs
/// around him, a speed buff no other monster casts.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class OrcWarbandTest
{
	private const string Grunt = "res://scenes/enemies/orc/orc_warrior.tscn";
	private const string Spearman = "res://scenes/enemies/orc/orc_spearman.tscn";
	private const string Shaman = "res://scenes/enemies/orc/orc_shaman.tscn";

	[TestCase]
	public async Task SpearmanThrowsAJavelinFromHisHandAtHisTarget()
	{
		using var runner = TestFactories.LoadScene(Spearman);
		await runner.SimulateFrames(2, 20);
		var spearman = (EnemyBase)runner.Scene();
		var throwing = spearman.Definition.Behavior.Combat.Actions.OfType<MonsterAttackAction>().First(action => action.Attack.IsRanged);
		var controller = spearman.GetNode<AttackController>("AttackController");
		// The throw locks onto a target before release, so place a player-group target straight ahead within range.
		Vector3 forward = (-spearman.GlobalTransform.Basis.Z with { Y = 0 }).Normalized();
		var target = new Node3D { Name = "AimTarget" };
		target.AddToGroup("player");
		spearman.GetParent().AddChild(target);
		target.GlobalPosition = spearman.GlobalPosition + forward * 7;
		AssertBool(controller.TryPrepareAttack(throwing.Attack, throwing.Style, throwing.Duration, out var attack)).IsTrue();
		AssertBool(controller.StartAttack(attack, new AttackStats(4, 8, 0), 4)).IsTrue();
		Node projectileParent = spearman.GetTree().CurrentScene ?? spearman.GetParent();
		var before = projectileParent.GetChildren().OfType<Projectile>().ToHashSet();
		// Cross the aim lock and the release in one step, without relying on frame timing.
		Vector3 hand = spearman.FindChild("Muzzle", true, false) is Node3D muzzle ? muzzle.GlobalPosition : Vector3.Inf;
		controller._PhysicsProcess(attack.Definition.HitWindowStart + .05);
		var javelin = projectileParent.GetChildren().OfType<Projectile>().Single(p => !before.Contains(p));
		AssertString(javelin.SceneFilePath).IsEqual("res://scenes/attacks/javelin_projectile.tscn");
		AssertFloat(javelin.Hit.MinDamage).IsEqual(4);
		AssertFloat((javelin.Direction with { Y = 0 }).Normalized().Dot(forward)).IsGreater(0.95f);
		// It leaves from the throwing hand, not from the default spot in front of the chest.
		AssertFloat(javelin.GlobalPosition.DistanceTo(hand)).IsLess(0.6f);
		ScenePool.Despawn(javelin);
		controller.CancelAttack();
		target.QueueFree();
	}

	[TestCase]
	public async Task ShamanHastesTheGruntBesideHim()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner, 4);
		harness.Player.InputController.SetPhysicsProcess(false);
		harness.Player.HurtBoxComponent.Invulnerable = true;
		var grunt = (EnemyBase)harness.Monsters.Spawn(TestFactories.Load<PackedScene>(Grunt), 3);
		var shaman = harness.Monsters.Spawn(TestFactories.Load<PackedScene>(Shaman), 7);
		harness.ExecuteCommand("automation", value: 1);
		float pace = grunt.Definition.MoveSpeed;
		// The haste lands on the grunt, so it runs faster than its own pace.
		await TestFactories.AwaitPhysicsUntil(runner, () => grunt.MovementComponent.Speed > pace * 1.05f, 8);
		AssertBool(GodotObject.IsInstanceValid(shaman)).IsTrue();
	}
}
