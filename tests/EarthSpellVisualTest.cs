namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Checks the earth workbench's visual lifecycle and distinct target placement.</summary>
[TestSuite, RequireGodotRuntime]
public partial class EarthSpellVisualTest
{
	[TestCase]
	public async Task GuardStylesUseDistinctMotionAndDust()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/earth_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (EarthSpellPreview)runner.Scene();
		preview.SelectSpell(3); preview.Cast();
		var barrier = preview.GetChildren().OfType<EarthSpellVisual>().Single();
		var panels = barrier.GetNode<Node3D>("StonePanels");
		AssertInt(panels.GetChildren().OfType<GpuParticles3D>().Count()).IsEqual(2);
		AssertInt(barrier.GetChildren().OfType<MeshInstance3D>().Count()).IsEqual(0);
		float yaw = panels.Rotation.Y;
		await runner.SimulateFrames(6);
		AssertBool(!Mathf.IsEqualApprox(yaw, panels.Rotation.Y)).IsTrue();
		preview.ClearGuard(); await runner.SimulateFrames(2);
		preview.SelectSpell(4); preview.Cast();
		var armor = preview.GetChildren().OfType<EarthSpellVisual>().Single();
		var spikes = armor.GetNode<Node3D>("ArmorSpikes");
		AssertInt(armor.FindChildren("*", "GPUParticles3D", true, false).Count).IsEqual(0);
		AssertInt(armor.GetChildren().OfType<MeshInstance3D>().Count()).IsEqual(0);
		var spike = (Node3D)spikes.GetChild(0);
		Vector3 original = spike.Position;
		await runner.SimulateFrames(6);
		AssertBool(!original.IsEqualApprox(spike.Position)).IsTrue();
	}

	[TestCase]
	public async Task RockfallRepeatsASingleTargetAndAddsCosmeticImpacts()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/earth_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (EarthSpellPreview)runner.Scene();
		preview.SetTargetCount(1);
		await runner.SimulateFrames(3);
		preview.Cast();
		var falls = preview.GetChildren().OfType<EarthSpellVisual>().ToArray();
		AssertInt(falls.Count(fall => !fall.Cosmetic)).IsEqual(3);
		AssertInt(falls.Count(fall => fall.Cosmetic)).IsEqual(2);
		AssertInt(falls.Where(fall => !fall.Cosmetic).Select(fall => fall.GlobalPosition).Distinct().Count()).IsEqual(1);
	}

	[TestCase]
	public async Task RockfallUsesDistinctTargetsAndTremorReturnsCameraToRest()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/earth_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (EarthSpellPreview)runner.Scene();
		preview.SetLevel(3); preview.Cast();
		var rocks = preview.GetChildren().OfType<EarthSpellVisual>().ToArray();
		AssertInt(rocks.Length).IsEqual(13);
		AssertInt(rocks.Count(effect => !effect.Cosmetic)).IsEqual(9);
		AssertInt(rocks.Where(effect => !effect.Cosmetic).Select(effect => effect.GlobalPosition).Distinct().Count()).IsEqual(3);
		preview.SelectSpell(1); preview.Cast();
		AssertBool(preview.PreviewWall.GetChildren().OfType<GpuParticles3D>().Single().Emitting).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
		AssertFloat(preview.Camera.HOffset).IsEqualApprox(0, .001);
		AssertFloat(preview.Camera.VOffset).IsEqualApprox(0, .001);
		await preview.ToSignal(preview.GetTree().CreateTimer(3.5), SceneTreeTimer.SignalName.Timeout);
		AssertInt(preview.GetChildren().OfType<EarthSpellVisual>().Count()).IsEqual(0);
	}

	[TestCase]
	public async Task DustStartsOnRockImpactAndSpikeEruption()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/earth_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (EarthSpellPreview)runner.Scene();
		preview.SetLevel(1); preview.Cast();
		var rock = preview.GetChildren().OfType<EarthSpellVisual>().First();
		var rockDust = rock.GetChildren().OfType<GpuParticles3D>().Single();
		var fragments = rock.GetNode<Node3D>("ImpactFragments");
		AssertBool(fragments.Visible).IsFalse();
		AssertInt(rock.GetChildren().OfType<MultiMeshInstance3D>().Single().Multimesh.InstanceCount).IsEqual(1);
		AssertBool(rockDust.Emitting).IsFalse();
		preview.SelectSpell(2); preview.Cast();
		var spikes = preview.GetChildren().OfType<EarthSpellVisual>().Single(e => e.Kind == EarthSpellVisual.EarthKind.Spikes);
		var spikeDust = spikes.GetChildren().OfType<GpuParticles3D>().ToArray();
		var cracks = spikes.GetChildren().OfType<MultiMeshInstance3D>().Single();
		AssertBool(cracks.Multimesh.InstanceCount > 0).IsTrue();
		AssertThat(cracks.Multimesh.GetInstanceTransform(0).Origin).IsEqual(new Vector3(0, .09f, 0));
		foreach (var spike in spikes.GetChildren().OfType<MeshInstance3D>().Where(n => n.Mesh is ArrayMesh))
		{
			AssertBool(spike.Position.Y < 0).IsTrue();
			AssertBool(new Vector2(spike.Position.X, spike.Position.Z).Dot(new Vector2(spike.Basis.Y.X, spike.Basis.Y.Z)) > 0).IsTrue();
		}
		AssertBool(spikeDust.Length > 1 && spikeDust.All(dust => !dust.Emitting)).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
		AssertBool(spikeDust.All(dust => dust.Emitting)).IsTrue();
		AssertBool(((ShaderMaterial)cracks.MaterialOverride).GetShaderParameter("progress").AsSingle() > 0).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
		AssertBool(rockDust.Emitting).IsTrue();
		AssertBool(fragments.Visible).IsTrue();
	}

	[TestCase]
	public async Task GuardsPersistAndDepleteInThePreview()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/earth_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (EarthSpellPreview)runner.Scene();
		foreach (int index in new[] { 3, 4 })
		{
			preview.SelectSpell(index); preview.Cast();
			var guard = preview.GetChildren().OfType<EarthSpellVisual>().Single();
			await preview.ToSignal(preview.GetTree().CreateTimer(1.9), SceneTreeTimer.SignalName.Timeout);
			AssertBool(GodotObject.IsInstanceValid(guard)).IsTrue();
			for (int hit = 0; hit < (index == 4 ? 6 : 4); hit++) preview.Reflect();
			await runner.SimulateFrames(2);
			AssertBool(GodotObject.IsInstanceValid(guard)).IsFalse();
		}
	}
}
