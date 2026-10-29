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
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/water_spell_preview.tscn");
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
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/water_spell_preview.tscn");
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
		foreach (int index in new[] { 2, 4 })
		{
			preview.SetLevel(3); preview.SelectSpell(index); preview.Cast();
			var guard = preview.GetChildren().OfType<WaterSpellVisual>().Single();
			if (index == 2) AssertBool(guard.Crystals.GetChildCount() >= 8).IsTrue();
			else AssertBool(guard.Serpent.Heads.Length == 2).IsTrue();
			preview.ClearGuard(); AssertBool(guard.Visible).IsFalse();
			await runner.SimulateFrames(2);
			AssertBool(GodotObject.IsInstanceValid(guard)).IsFalse();
		}
	}
	[TestCase]
	public async Task SnowstormScalesAndClipsWithoutFreezingTargets()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/water_spell_preview.tscn");
		await runner.SimulateFrames(5);
		var preview = (WaterSpellPreview)runner.Scene();
		preview.SetLevel(3); preview.SelectSpell(3); preview.Cast();
		var storm = preview.GetChildren().OfType<WaterSpellVisual>().Single();
		AssertFloat(storm.PreviewRadius).IsEqualApprox(6.5, .001);
		AssertInt(storm.Snowstorm.Snow.Amount).IsEqual(800);
		var material = (ShaderMaterial)storm.Snowstorm.Snow.MaterialOverride;
		AssertBool(material.GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		AssertBool(material.GetShaderParameter("flake_texture").AsGodotObject() is Texture2D).IsTrue();
		AssertFloat(material.GetShaderParameter("storm_radius").AsSingle()).IsEqualApprox(6.5, .001);
		AssertBool(storm.Timeline.Surfaces.All(layer => ((ShaderMaterial)layer.MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool())).IsTrue();
		AssertBool(preview.GetChildren().OfType<Node3D>().Where(node => node.IsInGroup("enemy"))
			.All(target => !target.GetChildren().OfType<FrozenPreview>().Any())).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
		AssertBool(GodotObject.IsInstanceValid(storm)).IsFalse();
	}

	[TestCase]
	public async Task WaterSerpentUsesTieredRibbonsAndKeepsMoving()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/water_spell_preview.tscn");
		await runner.SimulateFrames(5);
		var preview = (WaterSpellPreview)runner.Scene();
		preview.SelectSpell(4); preview.SetLevel(1); preview.Cast();
		var guard = preview.GetChildren().OfType<WaterSpellVisual>().Single();
		AssertBool(guard.Serpent.SecondaryRibbon.Visible).IsFalse();
		Vector3 before = guard.Serpent.Heads[0].Position;
		await runner.SimulateFrames(8);
		AssertBool(!before.IsEqualApprox(guard.Serpent.Heads[0].Position)).IsTrue();
		AssertInt(guard.Serpent.Droplets.Amount).IsEqual(40);
		preview.ClearGuard(); await runner.SimulateFrames(2);
		preview.SetLevel(3); preview.Cast();
		guard = preview.GetChildren().OfType<WaterSpellVisual>().Single();
		AssertBool(guard.Serpent.SecondaryRibbon.Visible).IsTrue();
		AssertInt(guard.Serpent.Droplets.Amount).IsEqual(72);
		AssertBool(((ShaderMaterial)guard.Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		preview.ClearGuard(); await runner.SimulateFrames(2);
		AssertBool(GodotObject.IsInstanceValid(guard)).IsFalse();
	}

}