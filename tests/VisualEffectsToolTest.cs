namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Visual diagnostics act on the active world and preserve independent effect settings.</summary>
[TestSuite, RequireGodotRuntime]
public class VisualEffectsToolTest
{
	[TestCase]
	public async Task GameplayVisualControlsChangeTheirEffects()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5, 20);
		var scene = runner.Scene();
		var tool = TestFactories.Tool<VisualEffectsTool>(scene);
		tool.Refresh();
		var popup = tool.Popup;
		var level = scene.GetNode<Level>("Level");
		var lighting = level.Lighting;
		bool originalXray = GameDebug.DoorXrayEnabled;
		TestFactories.Press(popup, "Door x-ray");
		AssertBool(GameDebug.DoorXrayEnabled).IsEqual(!originalXray);
		TestFactories.Press(popup, "Door x-ray");
		AssertBool(GameDebug.DoorXrayEnabled).IsEqual(originalXray);
		bool originalFog = lighting.IsEffectEnabled(EnvironmentalEffect.Fog);
		bool originalSsao = lighting.Environment.SsaoEnabled;
		bool originalBlur = tool.Menu.FocusBlur.Visible;
		TestFactories.Press(popup, "Atmospheric fog");
		TestFactories.Press(popup, "Ambient occlusion");
		TestFactories.Press(popup, "Focus blur");
		AssertBool(lighting.IsEffectEnabled(EnvironmentalEffect.Fog)).IsEqual(!originalFog);
		AssertBool(lighting.Environment.SsaoEnabled).IsEqual(!originalSsao);
		AssertBool(tool.Menu.FocusBlur.Visible).IsEqual(!originalBlur);
		var haze = level.Atmosphere.GetChildren().OfType<AtmosphereHaze>().Single();
		AssertBool(haze.IsProcessing() || haze.Visible).IsFalse();
		TestFactories.Press(popup, "Atmospheric fog");
		AssertBool(haze.IsProcessing() && haze.Visible).IsTrue();

		var particles = level.Atmosphere.GetChildren().OfType<AtmosphereParticles>().ToArray();
		var dust = particles.Single(layer => layer.Category == EnvironmentalEffect.Dust);
		var drips = particles.Single(layer => layer.Category == EnvironmentalEffect.Drips);
		TestFactories.Press(popup, "Dust and glimmer");
		AssertBool(dust.Visible || dust.Particles.Emitting || dust.CanProcess()).IsFalse();
		AssertBool(drips.Visible && drips.Particles.Emitting).IsTrue();
		TestFactories.Press(popup, "Drips and splashes");
		AssertBool(drips.Visible || drips.Particles.Emitting || drips.CanProcess()).IsFalse();
		TestFactories.Press(popup, "Dust and glimmer");
		TestFactories.Press(popup, "Drips and splashes");
		AssertBool(dust.Visible && dust.Particles.Emitting && drips.Visible && drips.Particles.Emitting).IsTrue();
		bool authoredWind = level.Airflow.Enabled;
		TestFactories.Press(popup, "Wind (banners and plants)");
		AssertBool(level.Airflow.IsActive || level.Airflow.IsProcessing()).IsFalse();
		AssertBool(level.Airflow.Enabled).IsEqual(authoredWind);
		TestFactories.Press(popup, "Wind (banners and plants)");
		AssertBool(level.Airflow.IsActive && level.Airflow.IsProcessing()).IsTrue();

		var mark = level.FindChildren("*", "", true, false).OfType<SurfaceMark>().First();
		TestFactories.Press(popup, "Surface marks (wet spots, soot)");
		AssertBool(mark.IsVisibleInTree() || mark.CanProcess()).IsFalse();
		TestFactories.Press(popup, "Surface marks (wet spots, soot)");
		AssertBool(mark.IsVisibleInTree() && mark.CanProcess()).IsTrue();

		var probe = level.GetChildren().OfType<LocalReflectionProbe>().Single();
		TestFactories.Press(popup, "Reflections");
		AssertBool(probe.Visible || probe.IsProcessing()).IsFalse();
		AssertThat(lighting.Environment.ReflectedLightSource).IsEqual(Godot.Environment.ReflectionSource.Disabled);
		lighting.IsDark = true;
		lighting.IsDark = false;
		AssertBool(probe.Visible || probe.IsProcessing()).IsFalse();
		TestFactories.Press(popup, "Reflections");
		await runner.SimulateFrames(2, 20);
		AssertBool(probe.Visible && probe.IsProcessing()).IsTrue();
	}

	[TestCase]
	public async Task ArenaDisablesUnavailableFocusBlur()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner);
		var tool = TestFactories.Tool<VisualEffectsTool>(runner.Scene());
		tool.Refresh();
		var popup = tool.Popup;
		int blur = Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == "Focus blur");
		int fog = Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == "Atmospheric fog");
		AssertBool(popup.IsItemDisabled(blur)).IsTrue();
		AssertBool(popup.IsItemDisabled(fog)).IsFalse();
	}
}