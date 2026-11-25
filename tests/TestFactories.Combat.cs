namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;

/// <summary>Combat-suite helpers shared by the harness tests.</summary>
public static partial class TestFactories
{
	/// <summary>
	/// Waits <paramref name="count"/> physics ticks, so bodies a test just moved or added register with the physics space
	/// before a contact query reads it.
	/// </summary>
	public static async Task AwaitPhysicsFrames(ISceneRunner runner, int count = 2)
	{
		var tree = runner.Scene().GetTree();
		for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
	}

	/// <summary>
	/// Returns the harness's practice target to full health, full poise and no stagger. A broken poise staggers the target,
	/// and a staggered defender takes extra damage, so tests that land several hits reset it between them.
	/// </summary>
	/// <summary>
	/// Turns <paramref name="monster"/> to face <paramref name="target"/>, then prepares and starts its first ranged
	/// attack action at <paramref name="hit"/>; false when the controller refuses it.
	/// </summary>
	public static async Task<bool> StartRangedAttack(ISceneRunner runner, EnemyBase monster, Node3D target, AttackStats hit)
	{
		monster.MovementComponent.SetLookAtDirection((target.GlobalPosition - monster.GlobalPosition) with { Y = 0 });
		monster.LookAt(target.GlobalPosition with { Y = monster.GlobalPosition.Y });
		await runner.SimulateFrames(2, 20);
		var action = monster.Definition.Behavior.Combat.Actions.OfType<MonsterAttackAction>().First(candidate => candidate.Attack.IsRanged);
		var controller = monster.AttackController;
		return controller.TryPrepareAttack(action.Attack, action.Style, action.Duration, out var attack)
			&& controller.StartAttack(attack, hit, 4);
	}

	/// <summary>The live slime patch nearest <paramref name="point"/>, or null.</summary>
	public static GooPatch NearestPatch(Node scene, Vector3 point) =>
		scene.GetTree().Root.FindChildren("*", "", true, false).OfType<GooPatch>().Where(patch => patch.IsPhysicsProcessing())
			.OrderBy(patch => patch.GlobalPosition.DistanceTo(point)).FirstOrDefault();

	public static HealthComponent ResetPracticeTarget(CombatHarness harness)
	{
		var target = (CombatPracticeTarget)harness.PracticeTarget;
		target.ActionGate.ResetForSimulation();
		target.HurtBox.Poise?.Restore();
		target.Health.SetHealth(target.Health.MaxHealth, target.Health.MaxHealth);
		return target.Health;
	}
}
