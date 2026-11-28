namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using static GdUnit4.Assertions;

/// <summary>One noise-driven flicker profile drives every flickering light, each from its own start offset.</summary>
[TestSuite, RequireGodotRuntime]
public class FlickerProfileTest
{
	private const string Presets = "res://scenes/effects/flicker_profiles/";

	private static FlickeringLight TorchLight() => TestFactories.AddToSceneTree(TestFactories.Torch()).GetNode<FlickeringLight>("FlickeringLight");
	private static float[] Samples(FlickerProfile profile, float step, int count) =>
		Enumerable.Range(0, count).Select(i => profile.Sample(i * step)).ToArray();

	[TestCase]
	public void TorchesSpawnedTogetherFlickerOutOfStep()
	{
		var lights = new[] { TorchLight(), TorchLight() };
		foreach (var light in lights) light._Process(.2);
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
		light.Profile = null;
		AssertBool(light.IsProcessing()).IsFalse();
		AssertFloat(light.LightEnergy).IsEqual(light.Energy);
	}

	/// <summary>A flicker dip can hide the impact light for a frame; the light must come back while the flame burns.</summary>
	[TestCase]
	public void FireballImpactLightRecoversFromFlickerDips()
	{
		var impact = TestFactories.Load<PackedScene>("res://scenes/magic/fireball/fireball_impact.tscn").Instantiate<FireballImpact>();
		// Swings the multiplier through zero several times within the flame.
		impact.FlickerProfile = new FlickerProfile { Noise = new FastNoiseLite { Frequency = 12f }, Min = -1f, Max = 1f };
		TestFactories.AddToSceneTree(impact);
		bool dipped = false, recovered = false;
		for (int frame = 0; frame < 40 && !recovered; frame++)
		{
			impact._Process(.02);
			if (!impact.ImpactLight.Visible) dipped = true;
			else if (dipped) recovered = true;
		}
		AssertBool(dipped).IsTrue();
		AssertBool(recovered).IsTrue();
	}

	[TestCase]
	public void FlameWaversAroundTheAuthoredEnergy()
	{
		AssertFloat(Samples(TestFactories.Load<FlickerProfile>(Presets + "flame.tres"), 1f / 60f, 600).Average()).IsBetween(.9f, 1.1f);
	}

	[TestCase]
	public void ElectricStrobeHoldsEachLevelForAStep()
	{
		var electric = TestFactories.Load<FlickerProfile>(Presets + "electric.tres");
		float step = 1f / electric.StepRate;
		for (int i = 0; i < 20; i++)
			AssertFloat(electric.Sample(i * step + step * .9f)).IsEqual(electric.Sample(i * step + step * .1f));
		var levels = Samples(electric, step, 200);
		AssertFloat(levels.Max() - levels.Min()).IsGreater(.6f);
	}

	[TestCase]
	public void UnassignedNoiseHoldsTheLightSteady()
	{
		AssertFloat(new FlickerProfile().Sample(3f)).IsEqual(1f);
	}
}
