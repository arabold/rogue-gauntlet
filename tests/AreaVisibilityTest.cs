namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Checks captured area boundaries against real blockers and openings.</summary>
[TestSuite, RequireGodotRuntime]
public partial class AreaVisibilityTest
{
	[TestCase]
	public async Task BoundaryStopsAtWallAndRemainsOpenInOtherDirections()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/earth_spell_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (EarthSpellPreview)runner.Scene();
		var mask = AreaVisibility.Capture(preview.GetWorld3D(), preview.Caster.GlobalPosition, 14);
		AssertBool(mask.Contains(new Vector3(2, 0, 4))).IsTrue();
		AssertBool(mask.Contains(new Vector3(6, 0, 6))).IsFalse();
		AssertBool(mask.Contains(new Vector3(6, 0, 0))).IsTrue();
		AssertBool(mask.Contains(new Vector3(-9, 0, 0))).IsFalse();
		foreach (int spell in new[] { 1, 2 })
		{
			preview.SelectSpell(spell); preview.Cast();
			var effect = preview.GetChildren().OfType<EarthSpellVisual>().Last();
			AssertBool(effect.Visibility.Contains(new Vector3(6, 0, 6))).IsFalse();
			AssertBool(effect.Visibility.Contains(new Vector3(6, 0, 0))).IsTrue();
			var crackOrWave = effect.GetChildren().OfType<GeometryInstance3D>()
				.Where(node => node.MaterialOverride is ShaderMaterial)
				.Select(node => (ShaderMaterial)node.MaterialOverride).ToArray();
			AssertBool(crackOrWave.Length > 0 && crackOrWave.All(material => material.GetShaderParameter("area_clip_enabled").AsBool())).IsTrue();
			var dust = effect.GetChildren().OfType<GpuParticles3D>().First();
			AssertBool(((ShaderMaterial)((QuadMesh)dust.DrawPass1).Material).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		}
	}

	[TestCase]
	public async Task FlameBurstClipsEveryLayerAndParticle()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/fireball_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (FireballPreview)runner.Scene();
		preview.SelectSpell(1); preview.Cast();
		var effect = preview.GetChildren().OfType<FireSpellVisual>().Single();
		AssertBool(effect.Visibility.Contains(new Vector3(6, 0, 6))).IsFalse();
		AssertBool(effect.Visibility.Contains(new Vector3(6, 0, 0))).IsTrue();
		AssertBool(effect.Timeline.Surfaces.All(layer => ((ShaderMaterial)layer.MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool())).IsTrue();
		AssertBool(effect.Timeline.Emitters.All(emitter => ((ShaderMaterial)emitter.MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool())).IsTrue();
	}
}