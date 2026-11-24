namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>A gliding acid blob leaves slime marks along its path that fade once it stops.</summary>
[TestSuite]
[RequireGodotRuntime]
public class TrailMarksTest
{
	private const string AcidBlob = "res://scenes/enemies/slime/acid_blob.tscn";

	[TestCase]
	public async Task AMovingBlobLeavesMarksThatFadeAfterItStops()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(runner);
		var blob = TestFactories.SpawnHarnessMonster(harness, AcidBlob, 8);
		var trail = blob.GetChildren().OfType<TrailMarksComponent>().Single();
		trail.Lifetime = .5f;
		trail.FadeDuration = .3f;
		int Visible() => trail.GetChildren().OfType<SurfaceMark>().Count(mark => mark.Modulate.A > .01f);
		AssertInt(Visible()).IsEqual(0);

		Vector3 start = blob.GlobalPosition;
		blob.EnemyBehaviorComponent.AIEnabled = true;
		blob.EnemyBehaviorComponent.SetTarget(harness.Player);
		blob.EnemyBehaviorComponent.SetBehavior(EnemyBehaviorState.Chasing);
		await TestFactories.AwaitPhysicsUntil(runner, () => blob.GlobalPosition.DistanceTo(start) > 3 * trail.Spacing, 3);
		AssertInt(Visible()).IsGreaterEqual(2);

		blob.EnemyBehaviorComponent.AIEnabled = false;
		blob.MovementComponent.Stop();
		await runner.SimulateFrames(60, 20);
		AssertInt(Visible()).IsEqual(0);
	}
}
