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

	/// <summary>
	/// Spell flicker is scene data now that the drivers have no default, so every fire and lightning light must keep a
	/// profile; ice lights burn steady by design.
	/// </summary>
	[TestCase]
	public void FireAndLightningSpellLightsFlicker()
	{
		string[] scenes =
		[
			"air/lightning_arc", "air/lightning_ball", "air/lightning_ball_impact", "air/storm_guard", "air/thunderstorm",
			"fire/ember_guard", "fire/flame_burst", "fire/meteor_shower", "fire/solar_flares", "fireball/fireball_impact", "fireball/fireball_visual",
		];
		foreach (string scene in scenes)
		{
			var root = AutoFree(TestFactories.Load<PackedScene>($"res://scenes/magic/{scene}.tscn").Instantiate());
			var flickers = root.FindChildren("*", "", true, false).Prepend(root).Where(node => node == root || node.Owner == root)
				.Select<Node, (bool Driver, FlickerProfile Profile)>(node => node switch
				{
					VisualEffectTimeline timeline => (true, timeline.FlickerProfile),
					FireballImpact impact => (true, impact.FlickerProfile),
					FlickeringLight light => (true, light.Profile),
					_ => default,
				}).Where(driver => driver.Driver).ToArray();
			AssertArray(flickers).OverrideFailureMessage($"{scene} has no flicker driver").IsNotEmpty();
			AssertBool(flickers.All(driver => driver.Profile != null)).OverrideFailureMessage($"{scene} has a steady light").IsTrue();
		}
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
