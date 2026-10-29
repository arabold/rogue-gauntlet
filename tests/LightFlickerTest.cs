namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>One noise-driven flicker resource drives every flickering light, each from its own start offset.</summary>
[TestSuite, RequireGodotRuntime]
public class LightFlickerTest
{
	private static LightFlicker Preset(string name) => GD.Load<LightFlicker>($"res://scenes/effects/light_flicker/{name}.tres");
	private static float[] Samples(LightFlicker flicker, float step = 1f / 60f, int count = 600) =>
		Enumerable.Range(0, count).Select(i => flicker.Sample(i * step, 0f)).ToArray();

	[TestCase]
	public async Task TorchesSpawnedTogetherFlickerOutOfStep()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/effects/world_effect_harness.tscn");
		await runner.SimulateFrames(2);
		var scene = GD.Load<PackedScene>("res://scenes/props/torch_mounted.tscn");
		var lights = Enumerable.Range(0, 2)
			.Select(_ => TestFactories.AddToSceneTree(scene.Instantiate<Node3D>()).GetNode<FlickerLight>("FlickerLight")).ToArray();
		await runner.SimulateFrames(10, 20);
		AssertObject(lights[0].Flicker).IsSame(Preset("torch"));
		AssertFloat(lights[0].LightEnergy).IsBetween(.25f, 1f);
		AssertFloat(lights[1].LightEnergy).IsBetween(.25f, 1f);
		AssertFloat(lights[0].LightEnergy).IsNotEqual(lights[1].LightEnergy);
	}

	[TestCase]
	public void FlameWaversAroundTheAuthoredEnergy()
	{
		var samples = Samples(Preset("flame"));
		AssertFloat(samples.Min()).IsGreaterEqual(.7f);
		AssertFloat(samples.Max()).IsLessEqual(1.3f);
		AssertFloat(samples.Average()).IsBetween(.9f, 1.1f);
	}

	[TestCase]
	public void ElectricStrobeHoldsEachLevelForAStep()
	{
		var electric = Preset("electric");
		float step = 1f / electric.StepRate;
		for (int i = 0; i < 20; i++)
			AssertFloat(electric.Sample(i * step + step * .9f, 0f)).IsEqual(electric.Sample(i * step + step * .1f, 0f));
		var levels = Samples(electric, step, 200);
		AssertFloat(levels.Min()).IsGreaterEqual(.3f);
		AssertFloat(levels.Max()).IsLessEqual(1.3f);
		AssertFloat(levels.Max() - levels.Min()).IsGreater(.6f);
	}

	[TestCase]
	public void UnassignedNoiseHoldsTheLightSteady()
	{
		AssertFloat(new LightFlicker().Sample(3f, 12f)).IsEqual(1f);
	}
}
