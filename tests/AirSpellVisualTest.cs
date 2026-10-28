namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Air workbench effects own their materials and clean up without changing gameplay.</summary>
[TestSuite, RequireGodotRuntime]
public partial class AirSpellVisualTest
{
	[TestCase]
	public async Task ComposedEffectsFadeIndependentlyAndGuardCanBeCleared()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/air_spell_preview.tscn", true);
		await runner.SimulateFrames(4);
		var preview = (AirSpellPreview)runner.Scene();
		preview.SelectSpell(2); preview.Cast(); preview.Cast();
		var waves = preview.GetChildren().OfType<VisualEffectInstance>().ToArray();
		AssertInt(waves.Length).IsEqual(2);
		AssertBool(waves[0].Timeline.Surfaces[0].MaterialOverride != waves[1].Timeline.Surfaces[0].MaterialOverride).IsTrue();
		AssertBool(((ShaderMaterial)waves[0].Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
		AssertBool(((ShaderMaterial)waves[0].Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("effect_fade").AsSingle() > 0).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(1.1), SceneTreeTimer.SignalName.Timeout);
		AssertBool(waves.All(w => !GodotObject.IsInstanceValid(w))).IsTrue();
		preview.SelectSpell(4); preview.Cast();
		var guard = preview.GetChildren().OfType<VisualEffectInstance>().Single();
		await preview.ToSignal(preview.GetTree().CreateTimer(1.1), SceneTreeTimer.SignalName.Timeout);
		AssertBool(GodotObject.IsInstanceValid(guard)).IsTrue();
		preview.ClearGuard();
		AssertBool(guard.Visible).IsFalse();
		await runner.SimulateFrames(2);
		AssertBool(GodotObject.IsInstanceValid(guard)).IsFalse();
	}
	[TestCase]
	public async Task StrategiesLinkDistinctVisibleDestinationsInDifferentWays()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/air_spell_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (AirSpellPreview)runner.Scene();
		Vector3 origin = Vector3.Up;
		Vector3[] points = [new(-2, 1, -1), new(-3, 1, 1), new(6, 1, 6)];
		var direct = preview.TargetStrategies[0].Select(preview.GetWorld3D(), origin, points, 3);
		var chain = preview.TargetStrategies[1].Select(preview.GetWorld3D(), origin, points, 3);
		AssertInt(direct.Count).IsEqual(2);
		AssertInt(chain.Count).IsEqual(2);
		AssertThat(direct[1].Start).IsEqual(origin);
		AssertThat(chain[1].Start).IsEqual(chain[0].End);
		AssertBool(chain.Select(segment => segment.End).Distinct().Count() == 2).IsTrue();
	}

	[TestCase]
	public async Task BallLightningTravelsAndFadesWithoutUsingChainArcs()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/air_spell_preview.tscn", true);
		await runner.SimulateFrames(4);
		var preview = (AirSpellPreview)runner.Scene();
		preview.SetTargetCount(1); preview.SetLevel(1); preview.SelectSpell(0); preview.Cast();
		var ball = preview.GetChildren().OfType<TravelingEffectVisual>().Single();
		Vector3 start = ball.GlobalPosition;
		AssertInt(preview.GetChildren().OfType<LightningArc>().Count()).IsEqual(0);
		AssertBool(((ShaderMaterial)ball.Trails[0].MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		AssertBool(((ShaderMaterial)ball.Sparks.MaterialOverride).GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
		AssertBool(ball.GlobalPosition.DistanceTo(start) > .5f).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout);
		AssertBool(ball.Sparks.Emitting).IsFalse();
		AssertBool(preview.GetChildren().OfType<VisualEffectInstance>().Any(effect => effect.Name == "LightningBallImpact")).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
		AssertBool(GodotObject.IsInstanceValid(ball)).IsFalse();
	}

	[TestCase]
	public async Task ThunderstormCracksMatchStrikesAndUseClippedBlueMaterials()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/air_spell_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (AirSpellPreview)runner.Scene();
		preview.SetLevel(3); preview.SelectSpell(3); preview.Cast();
		var arcs = preview.GetChildren().OfType<LightningArc>().ToArray();
		var cracks = preview.GetChildren().OfType<VisualEffectInstance>().Where(effect => effect.Timeline.Duration == 1.4f).ToArray();
		AssertBool(cracks.Length > 0).IsTrue();
		AssertInt(cracks.Length).IsEqual(arcs.Length);
		foreach (var crack in cracks)
		{
			var material = (ShaderMaterial)crack.Timeline.Surfaces[0].MaterialOverride;
			AssertBool(material.GetShaderParameter("area_clip_enabled").AsBool()).IsTrue();
			Vector3 color = material.GetShaderParameter("outer_color").AsVector3();
			AssertBool(color.Z > color.X && color.Z > color.Y).IsTrue();
		}
		await preview.ToSignal(preview.GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
		AssertBool(((ShaderMaterial)cracks[0].Timeline.Surfaces[0].MaterialOverride).GetShaderParameter("effect_fade").AsSingle() > 0).IsTrue();
		await preview.ToSignal(preview.GetTree().CreateTimer(2.4), SceneTreeTimer.SignalName.Timeout);
		AssertBool(cracks.All(crack => !GodotObject.IsInstanceValid(crack))).IsTrue();
	}

	[TestCase]
	public async Task HigherLevelGuardEmitsMoreArcsAndOwnsTheirLifetime()
	{
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/air_spell_preview.tscn", true);
		await runner.SimulateFrames(5);
		var preview = (AirSpellPreview)runner.Scene();
		var low = preview.GuardScene.Instantiate<StormGuardVisual>();
		var high = preview.GuardScene.Instantiate<StormGuardVisual>();
		low.Configure(1, 42); high.Configure(3, 42);
		preview.AddChild(low); preview.AddChild(high);
		await preview.ToSignal(preview.GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
		var lowArcs = low.ArcEmitter.GetChildren().OfType<LightningArc>().ToArray();
		var highArcs = high.ArcEmitter.GetChildren().OfType<LightningArc>().ToArray();
		AssertBool(lowArcs.Length > 0).IsTrue();
		AssertBool(highArcs.Length > lowArcs.Length).IsTrue();
		high.QueueFree();
		await runner.SimulateFrames(2);
		AssertBool(highArcs.All(arc => !GodotObject.IsInstanceValid(arc))).IsTrue();
		low.QueueFree();
	}

}
