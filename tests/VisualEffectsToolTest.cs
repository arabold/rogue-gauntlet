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
		var lighting = scene.GetNode<Level>("Level").Lighting;
		bool originalXray = GameDebug.DoorXrayEnabled;
		TestFactories.Press(popup, "Door x-ray");
		AssertBool(GameDebug.DoorXrayEnabled).IsEqual(!originalXray);
		TestFactories.Press(popup, "Door x-ray");
		AssertBool(GameDebug.DoorXrayEnabled).IsEqual(originalXray);
		bool originalFog = lighting.FogEnabled;
		bool originalSsao = lighting.Environment.SsaoEnabled;
		bool originalBlur = tool.Menu.FocusBlur.Visible;
		TestFactories.Press(popup, "Atmospheric fog");
		TestFactories.Press(popup, "Ambient occlusion");
		TestFactories.Press(popup, "Focus blur");
		AssertBool(lighting.FogEnabled).IsEqual(!originalFog);
		AssertBool(lighting.Environment.SsaoEnabled).IsEqual(!originalSsao);
		AssertBool(tool.Menu.FocusBlur.Visible).IsEqual(!originalBlur);
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