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
	public async Task ManualSamplesRespectBoundsAndRetireAfterEnd()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/effect_harness_navigation.tscn");
		await runner.SimulateFrames(1, 20);
		var root = TestFactories.AddToSceneTree(new Node());
		var trail = new RibbonTrail
		{
			Target = null,
			MaxPoints = 3,
			PointSpacing = 0.01f,
			PointLifetime = 0.08f,
			MaxLength = 0.35f,
			MeshUpdateInterval = 0,
		};
		root.AddChild(trail);

		AssertBool(trail.IsProcessing()).IsFalse();
		trail.Begin();
		AssertBool(trail.IsProcessing()).IsTrue();
		trail.Sample(Vector3.Zero);
		trail.Sample(Vector3.Forward * 0.2f);
		trail.Sample(Vector3.Forward * 0.4f);
		trail.Sample(Vector3.Forward * 0.6f);
		await runner.SimulateFrames(1, 20);

		var mesh = (ArrayMesh)trail.Mesh;
		var vertices = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		AssertInt(vertices.Length).IsEqual(4);

		trail.End();
		await runner.SimulateFrames(5, 20);

		AssertObject(trail.Mesh).IsNull();
		AssertBool(trail.IsProcessing()).IsFalse();
	}

	[TestCase]
	public async Task ProductionPlayerDodgeBuildsDashRibbon()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/movement_effect_preview.tscn");
		await runner.SimulateFrames(2, 20);
		var preview = (MovementEffectPreview)runner.Scene();
		preview.AutoPlayDemo = false;
		var player = preview.ActorSlot.GetChild<Player>(0);
		var effects = player.GetNode<MovementEffectsComponent>("MovementEffectsComponent");
		var screenVisibility = effects.ScreenVisibility;
		effects.ScreenVisibility = null;

		AssertBool(player.IsOnFloor()).IsTrue();
		AssertObject(player.DodgeComponent.TryDodge(Vector3.Forward)).IsEqual(DefenseRequestResult.Success);
		await runner.SimulateFrames(3, 20);

		AssertObject(effects.Streak.Mesh).IsNotNull();
		var vertices = ((ArrayMesh)effects.Streak.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		AssertInt(vertices.Length).IsGreater(0);

		effects.ScreenVisibility = screenVisibility;
		player.GlobalPosition += Vector3.Right * 3;
		player.ResetPhysicsInterpolation();
		await runner.SimulateFrames(1, 20);
		AssertBool(screenVisibility.GlobalPosition.IsEqualApprox(player.GlobalPosition)).IsTrue();
	}
}
