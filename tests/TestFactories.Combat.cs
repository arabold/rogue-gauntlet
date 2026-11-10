namespace RogueGauntlet.Tests;

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
	public static HealthComponent ResetPracticeTarget(CombatHarness harness)
	{
		var target = (CombatPracticeTarget)harness.PracticeTarget;
		target.ActionGate.ResetForSimulation();
		target.HurtBox.Poise?.Restore();
		target.Health.SetHealth(target.Health.MaxHealth, target.Health.MaxHealth);
		return target.Health;
	}
}
