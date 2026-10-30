namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>One noise-driven flicker resource drives every flickering light, each from its own start offset.</summary>
[TestSuite, RequireGodotRuntime]
public class LightFlickerTest
{
	private const string Harness = "res://scenes/debug/effects/world_effect_harness.tscn";
	private const string Presets = "res://scenes/effects/light_flicker/";

	private static FlickerLight TorchLight() => TestFactories.AddToSceneTree(TestFactories.Torch()).GetNode<FlickerLight>("FlickerLight");
	private static float[] Samples(LightFlicker flicker, float step, int count) =>
		Enumerable.Range(0, count).Select(i => flicker.Sample(i * step)).ToArray();

	[TestCase]
	public async Task TorchesSpawnedTogetherFlickerOutOfStep()
	{
		using var runner = TestFactories.LoadScene(Harness);
		await runner.SimulateFrames(2);
		var lights = new[] { TorchLight(), TorchLight() };
		await runner.SimulateFrames(10, 20);
		AssertString(lights[0].Flicker.ResourcePath).IsEqual(Presets + "torch.tres");
		AssertFloat(lights[0].LightEnergy).IsNotEqual(lights[1].LightEnergy);
	}

	[TestCase]
	public void OnlyVisibleLightsFlicker()
	{
		var light = TorchLight();
		var torch = light.GetParent<Node3D>();
		AssertBool(light.IsProcessing()).IsTrue();
		torch.Visible = false;
		AssertBool(light.IsProcessing()).IsFalse();
		torch.Visible = true;
		AssertBool(light.IsProcessing()).IsTrue();
		light.Flicker = null;
		AssertBool(light.IsProcessing()).IsFalse();
		AssertFloat(light.LightEnergy).IsEqual(light.Energy);
	}

	/// <summary>Spell flicker is scene data now that the drivers have no default, so each scene must keep its preset.</summary>
	[TestCase("res://scenes/magic/air/lightning_arc.tscn", "electric")]
	[TestCase("res://scenes/magic/air/lightning_ball.tscn", "electric")]
	[TestCase("res://scenes/magic/air/lightning_ball_impact.tscn", "electric")]
	[TestCase("res://scenes/magic/air/storm_guard.tscn", "electric")]
	[TestCase("res://scenes/magic/air/thunderstorm.tscn", "electric")]
	[TestCase("res://scenes/magic/fire/ember_guard.tscn", "flame")]
	[TestCase("res://scenes/magic/fire/flame_burst.tscn", "flame")]
	[TestCase("res://scenes/magic/fire/meteor_shower.tscn", "flame")]
	[TestCase("res://scenes/magic/fire/solar_flares.tscn", "flame")]
	[TestCase("res://scenes/magic/fireball/fireball_impact.tscn", "flame")]
	[TestCase("res://scenes/magic/fireball/fireball_visual.tscn", "flame")]
	public void SpellScenesKeepTheirFlicker(string path, string preset)
	{
		var root = AutoFree(TestFactories.Load<PackedScene>(path).Instantiate());
		var flickers = new List<LightFlicker>();
		foreach (var node in root.FindChildren("*", "", true, false).Prepend(root).Where(node => node == root || node.Owner == root))
		{
			if (node is VisualEffectTimeline timeline) flickers.Add(timeline.LightFlicker);
			else if (node is FireballImpact impact) flickers.Add(impact.LightFlicker);
			else if (node is FlickerLight light) flickers.Add(light.Flicker);
		}
		AssertArray(flickers.ToArray()).IsNotEmpty();
		foreach (var flicker in flickers) AssertString(flicker?.ResourcePath).IsEqual($"{Presets}{preset}.tres");
	}

	/// <summary>A flicker dip can hide the impact light for a frame; the light must come back while the flame burns.</summary>
	[TestCase]
	public async Task FireballImpactLightRecoversFromFlickerDips()
	{
		using var runner = TestFactories.LoadScene(Harness);
		await runner.SimulateFrames(2);
		var impact = GD.Load<PackedScene>("res://scenes/magic/fireball/fireball_impact.tscn").Instantiate<FireballImpact>();
		// Swings the multiplier through zero several times within the flame.
		impact.LightFlicker = new LightFlicker { Noise = new FastNoiseLite { Frequency = 12f }, Min = -1f, Max = 1f };
		TestFactories.AddToSceneTree(impact);
		bool dipped = false, recovered = false;
		for (int frame = 0; frame < 40 && !recovered; frame++)
		{
			await runner.SimulateFrames(1, 20);
			if (!impact.ImpactLight.Visible) dipped = true;
			else if (dipped) recovered = true;
		}
		AssertBool(dipped).IsTrue();
		AssertBool(recovered).IsTrue();
	}

	[TestCase]
	public void FlameWaversAroundTheAuthoredEnergy()
	{
		AssertFloat(Samples(TestFactories.Load<LightFlicker>(Presets + "flame.tres"), 1f / 60f, 600).Average()).IsBetween(.9f, 1.1f);
	}

	[TestCase]
	public void ElectricStrobeHoldsEachLevelForAStep()
	{
		var electric = TestFactories.Load<LightFlicker>(Presets + "electric.tres");
		float step = 1f / electric.StepRate;
		for (int i = 0; i < 20; i++)
			AssertFloat(electric.Sample(i * step + step * .9f)).IsEqual(electric.Sample(i * step + step * .1f));
		var levels = Samples(electric, step, 200);
		AssertFloat(levels.Max() - levels.Min()).IsGreater(.6f);
	}

	[TestCase]
	public void UnassignedNoiseHoldsTheLightSteady()
	{
		AssertFloat(new LightFlicker().Sample(3f)).IsEqual(1f);
	}
}
