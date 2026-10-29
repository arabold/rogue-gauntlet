namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Fire spell studies reflect their targeting and guard roles without changing gameplay health.</summary>
[TestSuite, RequireGodotRuntime]
public partial class FireSpellVisualTest
{
	[TestCase]
	public async Task FireLevelsAddOrbitsAndWispsWithoutGrowingGuard()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/fireball_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (FireballPreview)runner.Scene();
		for (int level = 1; level <= 3; level++)
		{
			preview.SetLevel(level); preview.SelectSpell(3); preview.Cast();
			var sweep = preview.GetChildren().OfType<FireSpellVisual>().Last(effect => effect.Sweep != null);
			AssertInt(sweep.Sweep.Orbits.Count(orbit => orbit.Visible)).IsEqual(level);
			AssertInt(sweep.Sweep.Emitters.Count(emitter => emitter.Emitting)).IsEqual(level);
			AssertThat(sweep.Scale).IsEqual(Vector3.One);
			AssertFloat(sweep.Sweep.Heads[0].Position.X).IsEqualApprox(2.6, .001);
			preview.SelectSpell(2); preview.Cast();
			var guard = preview.GetChildren().OfType<FireSpellVisual>().Last(effect => effect.Guard != null);
			AssertThat(guard.Scale).IsEqual(Vector3.One);
			AssertInt(guard.Guard.Embers[0].Amount).IsEqual(level * 16);
			AssertInt(((ShaderMaterial)guard.Guard.Surfaces[1].MaterialOverride).GetShaderParameter("wisp_count").AsInt32()).IsEqual(level + 1);
			preview.ClearGuard();
		}
	}

	[TestCase]
	public async Task AreaPreviewExcludesTargetsBehindWalls()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/fireball_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (FireballPreview)runner.Scene();
		preview.SetTargetCount(1);
		preview.SetLevel(3);
		var target = preview.GetChildren().OfType<Node3D>().Single(node => node.IsInGroup("enemy"));
		var wall = preview.DebugStage.WestWall;
		wall.Position = target.Position * .5f + Vector3.Up * 2;
		wall.Rotation = new Vector3(0, -.4f, 0);
		await runner.SimulateFrames(4);
		preview.SelectSpell(1);
		preview.Cast();
		AssertBool(preview.Controls.GetChildren().OfType<Label>().Last().Text.Contains("0 target(s) in area")).IsTrue();
	}

	[TestCase]
	public async Task AreaBurstIsCenteredAndGrowsWithoutATargetCap()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/fireball_preview.tscn");
		await runner.SimulateFrames(4);
		var scene = runner.Scene();
		var preview = (FireballPreview)scene;
		preview.SetTargetCount(5);
		preview.SelectSpell(1);
		float[] radii = [3.5f, 5f, 6.5f];
		for (int level = 1; level <= 3; level++)
		{
			preview.SetLevel(level);
			preview.Cast();
			var effects = scene.GetChildren().OfType<FireSpellVisual>().Where(effect => effect.Burst != null).ToArray();
			AssertInt(effects.Length).IsEqual(level);
			AssertThat(effects.Last().GlobalPosition).IsEqual(scene.GetNode<Node3D>("Caster").GlobalPosition);
			AssertFloat(effects.Last().PreviewRadius).IsEqualApprox(radii[level - 1], .001);
			AssertInt(effects.Last().Timeline.Surfaces.Length).IsEqual(1);
			AssertInt(effects.Last().Timeline.Emitters.Length).IsEqual(0);
			AssertBool(effects.Last().Timeline.Surfaces[0].Mesh.GetAabb().Size.Y > 1f).IsTrue();
			AssertFloat(((ShaderMaterial)effects.Last().Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("brightness").AsSingle()).IsEqualApprox(1.2 + .35 * level, .001);
		}
		AssertBool(scene.GetChildren().OfType<Node3D>().Where(n => n.IsInGroup("enemy"))
			.All(n => Mathf.IsEqualApprox(n.GetNode<HealthComponent>("HealthComponent").CurrentHealth, 100))).IsTrue();
		AssertBool(preview.Controls.GetChildren().OfType<Label>().Last().Text.Contains("5 target(s) in area")).IsTrue();
		preview.SetTargetCount(0);
		await runner.SimulateFrames(3);
		preview.Cast();
		AssertInt(scene.GetChildren().OfType<FireSpellVisual>().Count(effect => effect.Burst != null)).IsEqual(4);
		AssertBool(preview.Controls.GetChildren().OfType<Label>().Last().Text.Contains("0 target(s) in area")).IsTrue();
	}

	[TestCase]
	public async Task SolarSweepIsCenteredAndMeteorShowerRepeatsTargets()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/fireball_preview.tscn");
		await runner.SimulateFrames(4);
		var scene = runner.Scene();
		var preview = (FireballPreview)scene;
		preview.SetLevel(3);
		preview.SelectSpell(3); preview.Cast();
		var sweep = scene.GetChildren().OfType<FireSpellVisual>().Single(effect => effect.Sweep != null);
		AssertThat(sweep.GlobalPosition).IsEqual(scene.GetNode<Node3D>("Caster").GlobalPosition);
		AssertBool(sweep.Visibility != null).IsTrue();
		preview.SelectSpell(4); preview.Cast();
		var meteors = scene.GetChildren().OfType<FireSpellVisual>().Where(effect => effect.Meteor != null).ToArray();
		AssertInt(meteors.Length).IsEqual(21);
		AssertInt(meteors.Count(effect => !effect.Cosmetic)).IsEqual(9);
		AssertInt(meteors.Count(effect => effect.Cosmetic)).IsEqual(12);
		AssertBool(meteors.Where(effect => effect.Cosmetic).All(effect => effect.Timeline.Lights.Length == 0 && effect.Meteor.GroundCracks == null)).IsTrue();
		AssertInt(meteors.Where(effect => !effect.Cosmetic).Select(effect => effect.GlobalPosition).Distinct().Count()).IsEqual(3);
		await scene.ToSignal(scene.GetTree().CreateTimer(.55), SceneTreeTimer.SignalName.Timeout);
		float previousAngle = sweep.Sweep.Arms[0].Rotation.Y;
		await scene.ToSignal(scene.GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
		AssertBool(Mathf.Abs(Mathf.AngleDifference(previousAngle, sweep.Sweep.Arms[0].Rotation.Y)) > .65f).IsTrue();
		AssertInt(sweep.Sweep.Orbits.Count(orbit => orbit.Visible)).IsEqual(3);
		var landed = meteors.First(effect => !effect.Cosmetic);
		AssertBool(landed.Meteor.Meteor.Visible).IsTrue();
		AssertBool(landed.Meteor.Emitters.All(emitter => !emitter.Emitting)).IsTrue();
		var stone = (BaseMaterial3D)landed.Meteor.Rock.MaterialOverride;
		AssertInt((int)stone.Transparency).IsEqual((int)BaseMaterial3D.TransparencyEnum.Alpha);
		AssertBool(stone.AlbedoColor.A < 1f && stone.AlbedoColor.A > 0f).IsTrue();
		AssertBool(((ShaderMaterial)landed.Meteor.Shell.MaterialOverride).GetShaderParameter("effect_fade").AsSingle() < 1f).IsTrue();
		var trail = sweep.GetNode<MagicRibbonTrail>("Orbit0/Trail");
		AssertThat(trail.GlobalTransform).IsEqual(Transform3D.Identity);
		var vertices = ((ArrayMesh)trail.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		AssertBool(((vertices[0] + vertices[1]) * .5f).DistanceTo(sweep.Sweep.Heads[0].GlobalPosition) < .6f).IsTrue();
		await scene.ToSignal(scene.GetTree().CreateTimer(.35), SceneTreeTimer.SignalName.Timeout);
		AssertBool(scene.GetChildren().OfType<FireballImpact>().Any()).IsTrue();
		AssertBool(scene.GetChildren().OfType<VisualEffectInstance>().Any(effect => effect.Name == "LavaCracks")).IsTrue();
		AssertBool(scene.GetChildren().OfType<Node3D>().Where(n => n.IsInGroup("enemy"))
			.All(n => Mathf.IsEqualApprox(n.GetNode<HealthComponent>("HealthComponent").CurrentHealth, 100))).IsTrue();
		await scene.ToSignal(scene.GetTree().CreateTimer(3.2), SceneTreeTimer.SignalName.Timeout);
		AssertInt(scene.GetChildren().OfType<FireSpellVisual>().Count()).IsEqual(0);
		AssertInt(scene.GetChildren().OfType<FireballImpact>().Count()).IsEqual(0);
		AssertInt(scene.GetChildren().OfType<VisualEffectInstance>().Count(effect => effect.Name == "LavaCracks")).IsEqual(0);
	}

	[TestCase]
	public async Task GuardPersistsUntilDepletedOrClearedAndReflectionsAreHarmless()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/fireball_preview.tscn");
		await runner.SimulateFrames(4);
		var scene = runner.Scene();
		var preview = (FireballPreview)scene;
		preview.SelectSpell(2);
		preview.Cast();
		var guard = scene.GetChildren().OfType<FireSpellVisual>().Single();
		var emberEmitter = guard.Guard.Embers.Single();
		var emberProcess = (ParticleProcessMaterial)emberEmitter.ProcessMaterial;
		AssertInt((int)emberProcess.EmissionShape).IsEqual((int)ParticleProcessMaterial.EmissionShapeEnum.SphereSurface);
		AssertFloat(emberProcess.EmissionSphereRadius).IsEqualApprox(1.55, .001);
		AssertThat(emberEmitter.Position).IsEqual(new Vector3(0, 1.1f, 0));
		AssertInt(emberEmitter.Amount).IsEqual(16);
		preview.PreviewReflection();
		preview.PreviewReflection();
		await runner.SimulateFrames(4);
		var material = (ShaderMaterial)guard.Guard.Surfaces[0].MaterialOverride;
		AssertBool(material.GetShaderParameter("hit_flash").AsSingle() > 0).IsTrue();
		await scene.ToSignal(scene.GetTree().CreateTimer(4.2), SceneTreeTimer.SignalName.Timeout);
		AssertBool(GodotObject.IsInstanceValid(guard)).IsTrue();
		AssertBool(scene.GetChildren().OfType<Node3D>().Where(n => n.IsInGroup("enemy"))
			.All(n => Mathf.IsEqualApprox(n.GetNode<HealthComponent>("HealthComponent").CurrentHealth, 100))).IsTrue();
		preview.PreviewReflection();
		preview.PreviewReflection();
		await runner.SimulateFrames(2);
		AssertBool(GodotObject.IsInstanceValid(guard)).IsFalse();
		preview.Cast();
		var replacement = scene.GetChildren().OfType<FireSpellVisual>().Single();
		preview.ClearGuard();
		await runner.SimulateFrames(2);
		AssertBool(GodotObject.IsInstanceValid(replacement)).IsFalse();
	}
}