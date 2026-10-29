namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Shared timelines and falling motion keep authored spell components in sync.</summary>
[TestSuite, RequireGodotRuntime]
public partial class VisualEffectCompositionTest
{
	[TestCase]
	public async Task DelayedIceCrystalsUseTheirEffectsClock()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/water_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (WaterSpellPreview)runner.Scene();
		var nova = preview.NovaScene.Instantiate<WaterSpellVisual>();
		nova.ConfigureLevel(2); nova.Timeline.StartDelay = .3f;
		preview.AddChild(nova);
		AssertBool(nova.Crystals.Timeline == nova.Timeline).IsTrue();
		var crystal = nova.Crystals.GetChildren().OfType<MeshInstance3D>().First();
		await preview.ToSignal(preview.GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
		AssertBool(nova.Timeline.Started).IsFalse();
		AssertThat(crystal.Scale).IsEqual(Vector3.Zero);
		await preview.ToSignal(preview.GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
		AssertBool(nova.Timeline.Started && nova.Timeline.Progress > 0).IsTrue();
		AssertBool(crystal.Scale.Y > 0).IsTrue();
	}

	[TestCase]
	public async Task FallingMotionSignalsOneImpactForEarthAndFire()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/earth_spell_preview.tscn");
		await runner.SimulateFrames(4);
		var preview = (EarthSpellPreview)runner.Scene();
		var rock = preview.Effects[0].Instantiate<EarthSpellVisual>();
		rock.ConfigureLevel(1); rock.Fall.FlightTime = .12f; rock.Timeline.StartDelay = .1f;
		int rockHits = 0; rock.Fall.Impact += () => rockHits++; preview.AddChild(rock);
		var meteor = GD.Load<PackedScene>("res://scenes/magic/fire/meteor_debris.tscn").Instantiate<FireSpellVisual>();
		meteor.ConfigureLevel(1); meteor.Meteor.Fall.FlightTime = .12f; meteor.Timeline.StartDelay = .1f;
		int meteorHits = 0; meteor.Meteor.Fall.Impact += () => meteorHits++; preview.AddChild(meteor);
		await preview.ToSignal(preview.GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
		AssertInt(rockHits).IsEqual(0); AssertInt(meteorHits).IsEqual(0);
		await preview.ToSignal(preview.GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
		AssertInt(rockHits).IsEqual(1); AssertInt(meteorHits).IsEqual(1);
		AssertBool(rock.Fall.Body.Visible).IsFalse();
		AssertBool(meteor.Meteor.Fall.Body.Visible).IsTrue();
	}
}