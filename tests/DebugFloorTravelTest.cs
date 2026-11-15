namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The shared Level menu exposes floor selection in gameplay and disables it in the arena.</summary>
[TestSuite, RequireGodotRuntime]
public class DebugFloorTravelTest
{
	[TestCase]
	public async Task TheFloorDialogOpensBesideRestartAndRejectsTheCurrentFloor()
	{
		using var runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(5, 20);
		var scene = runner.Scene();
		var level = scene.GetNode<Level>("Level");
		var tool = TestFactories.Tool<LevelTool>(scene);
		var popup = tool.Popup;
		int Index(string name) => Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == name);
		AssertInt(Index("Change floor...")).IsEqual(Index("Restart level") + 1);
		TestFactories.Press(popup, "Change floor...");
		var dialog = tool.Menu.GetNode<ConfirmationDialog>("ChangeFloorDialog");
		AssertBool(dialog.Visible).IsTrue();
		var input = dialog.FindChildren("FloorNumber", nameof(SpinBox), true, false).OfType<SpinBox>().Single();
		AssertBool(dialog.GetOkButton().Disabled).IsTrue();
		input.Value = level.Depth + 1;
		AssertBool(dialog.GetOkButton().Disabled).IsFalse();
		input.Value = 1_000_000;
		AssertThat(input.Value).IsEqual(1_000_000d);
		AssertBool(dialog.GetOkButton().Disabled).IsFalse();
		input.Value = level.Depth;
		AssertBool(dialog.GetOkButton().Disabled).IsTrue();
		ulong seed = GameSession.Instance.Run.RunSeed;
		AssertBool(tool.ChangeFloor(0)).IsFalse();
		AssertBool(tool.ChangeFloor(level.Depth)).IsFalse();
		AssertThat(GameSession.Instance.ActiveDungeonDepth).IsEqual(level.Depth);
		AssertThat(GameSession.Instance.Run.RunSeed).IsEqual(seed);
		dialog.Hide();
	}

	[TestCase]
	public async Task TheArenaDisablesFloorTravel()
	{
		using var runner = TestFactories.LoadCombatHarness();
		await TestFactories.SettleCombatHarness(runner);
		var tool = TestFactories.Tool<LevelTool>(runner.Scene());
		tool.Refresh();
		var popup = tool.Popup;
		int index = Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == "Change floor...");
		AssertBool(popup.IsItemDisabled(index)).IsTrue();
		AssertBool(tool.ChangeFloor(5)).IsFalse();
	}
}