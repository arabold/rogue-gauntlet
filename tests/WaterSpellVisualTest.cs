namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Water studies remain visual-only and restore the actors they temporarily freeze.</summary>
[TestSuite, RequireGodotRuntime]
public partial class WaterSpellVisualTest
{
	[TestCase]
	public async Task FrostNovaFreezesVisibleActorsAndRestoresTheirAppearance()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/water_spell_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (WaterSpellPreview)runner.Scene();
		var targets = preview.GetChildren().OfType<Node3D>().Where(n => n.IsInGroup("enemy")).ToArray();
		targets[0].Position = new Vector3(4.4f, 0, 4.4f);
		await runner.SimulateFrames(3);
		var actor = targets[1].GetChildren().OfType<EnemyBase>().Single();
		var root = actor.WorldEffects.SurfaceController.Target;
		var mesh = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
			.First(m => !VisualHelper.IsInHelperSubtree(m) && m.Mesh != null && m.GetActiveMaterial(0) is BaseMaterial3D);
		Material original = mesh.GetActiveMaterial(0);
		var animation = actor.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().First();
		float originalSpeed = animation.SpeedScale;
		preview.SetLevel(3); preview.SelectSpell(1); preview.Cast();
		var nova = preview.GetChildren().OfType<WaterSpellVisual>().Single();
		AssertFloat(nova.PreviewRadius).IsEqualApprox(6.5, .001);
		AssertInt(targets[0].GetChildren().OfType<FrozenPreview>().Count()).IsEqual(0);
		await preview.ToSignal(preview.GetTree().CreateTimer(.6), SceneTreeTimer.SignalName.Timeout);
		AssertBool(mesh.GetActiveMaterial(0) is ShaderMaterial).IsTrue();
		AssertFloat(animation.SpeedScale).IsEqual(0);
		var crystal = nova.Crystals.GetChildren().OfType<MeshInstance3D>().First();
		AssertBool(((ShaderMaterial)crystal.Mesh.SurfaceGetMaterial(0)).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(2.2), SceneTreeTimer.SignalName.Timeout);
		AssertBool(mesh.GetActiveMaterial(0) == original).IsTrue();
		AssertFloat(animation.SpeedScale).IsEqual(originalSpeed);
	}

	[TestCase]
	public async Task LanceAndGuardsUseSharedVisualsAndCleanUp()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/water_spell_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (WaterSpellPreview)runner.Scene();
		preview.SetTargetCount(1); preview.SetLevel(1); preview.SelectSpell(0); preview.Cast();
		var lance = preview.GetChildren().OfType<TravelingEffectVisual>().Single();
		AssertBool(lance.GroundImpact).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.35), SceneTreeTimer.SignalName.Timeout);
		var impact = preview.GetChildren().OfType<WaterSpellVisual>().Single();
		AssertFloat(impact.GlobalPosition.Y).IsEqualApprox(preview.Caster.GlobalPosition.Y, .001);
		var crystal = impact.Crystals.GetChildren().OfType<MeshInstance3D>().First();
		AssertBool(((ShaderMaterial)crystal.Mesh.SurfaceGetMaterial(0)).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
		AssertBool(!GodotObject.IsInstanceValid(lance) && !GodotObject.IsInstanceValid(impact)).IsTrue();
		for (int index = 2; index < 5; index++)
		{
			preview.SetLevel(3); preview.SelectSpell(index); preview.Cast();
			var guard = preview.GetChildren().OfType<WaterSpellVisual>().Single();
			AssertBool(guard.Crystals.GetChildCount() >= 8).IsTrue();
			preview.ClearGuard(); AssertBool(guard.Visible).IsFalse();
			await runner.SimulateFrames(2);
			AssertBool(GodotObject.IsInstanceValid(guard)).IsFalse();
		}
	}
}
