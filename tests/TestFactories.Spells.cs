namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;

/// <summary>Waits shared by the spell suites.</summary>
public static partial class TestFactories
{
	/// <summary>
	/// Waits until a targeted cast has resolved: no strike still pending and no projectile still in flight, so every hit
	/// it could deal has had its chance.
	/// </summary>
	public static Task AwaitTargetedSpellSettled(ISceneRunner runner, TargetedSpell spell)
	{
		var tree = runner.Scene().GetTree();
		return AwaitPhysicsUntil(runner, () => !spell.IsPhysicsProcessing()
			&& !tree.GetNodesInGroup("projectile").OfType<Projectile>().Any(projectile => projectile.IsPhysicsProcessing()));
	}
}