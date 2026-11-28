namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Manual trails keep bounded geometry, then stop processing after their authored tail retires.</summary>
[TestSuite]
[RequireGodotRuntime]
public class RibbonTrailTest
{
	[TestCase]
	public void ManualSamplesRespectBoundsAndRetireAfterEnd()
	{
		var trail = TestFactories.AddToSceneTree(new RibbonTrail
		{
			Target = null,
			MaxPoints = 3,
			PointSpacing = 0.01f,
			PointLifetime = 0.08f,
			MaxLength = 0.35f,
			MeshUpdateInterval = 0,
		});

		trail.Begin();
		trail.Sample(Vector3.Zero);
		trail.Sample(Vector3.Forward * 0.2f);
		trail.Sample(Vector3.Forward * 0.4f);
		trail.Sample(Vector3.Forward * 0.6f);
		trail._Process(.02);

		var vertices = ((ArrayMesh)trail.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		AssertInt(vertices.Length).IsEqual(4);

		trail.End();
		for (int frame = 0; frame < 5; frame++) trail._Process(.02);
		AssertObject(trail.Mesh).IsNull();
		AssertBool(trail.IsProcessing()).IsFalse();
	}

	[TestCase]
	public async Task ProductionPlayerDodgeBuildsDashRibbon()
	{
		using var runner = TestFactories.LoadCombatHarness();
		var player = (await TestFactories.SettleCombatHarness(runner)).Player;
		var effects = player.GetNode<MovementEffectsComponent>("MovementEffectsComponent");
		// Headless nothing is on screen, so drop the on-screen gate that would skip the ribbon.
		effects.ScreenVisibility = null;

		AssertBool(player.IsOnFloor()).IsTrue();
		// Sideways, clear of the practice dummy ahead.
		AssertObject(player.DodgeComponent.TryDodge(Vector3.Right)).IsEqual(DefenseRequestResult.Success);
		await TestFactories.AwaitPhysicsFrames(runner, 3);

		AssertObject(effects.Streak.Mesh).IsNotNull();
		var vertices = ((ArrayMesh)effects.Streak.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		AssertInt(vertices.Length).IsGreater(0);
	}
}
