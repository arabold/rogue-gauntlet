namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using System.Linq;
using System.Threading.Tasks;
using static GdUnit4.Assertions;

/// <summary>X-ray staging uses closed production props and local occlusion controls, preserving game-wide settings.</summary>
[TestSuite, RequireGodotRuntime]
public class WorldEffectXrayHarnessTest
{
	[TestCase(4), TestCase(5)]
	public async Task ProductionPropIsBehindAnOpaqueWallAndWallTogglePreservesItsState(int index)
	{
		bool doorFlag = GameDebug.DoorXrayEnabled, lootFlag = GameDebug.LootXrayEnabled;
		using var runner = ISceneRunner.Load("res://scenes/debug/effects/world_effect_harness.tscn", true);
		await runner.SimulateFrames(2);
		var harness = (WorldEffectHarness)runner.Scene(); harness.SelectEffect(index);
		await runner.SimulateFrames(3);
		AssertBool(harness.IsXrayPreview).IsTrue(); AssertBool(harness.PreviewOccluder.Visible).IsTrue();
		AssertInt(harness.PreviewOccluder.GetUsedCells().Count).IsEqual(2);
		AssertBool(harness.PreviewOccluder.GetUsedCells().All(cell => cell.Z > 0)).IsTrue();
		bool RowVisible(string label) => harness.Controls.GetChildren().OfType<Container>()
			.Single(c => c.GetChild(0) is Label l && l.Text == label).Visible;
		AssertBool(RowVisible("Wall variant")).IsTrue(); AssertBool(RowVisible("Reveal glow")).IsFalse();
		if (index == 4)
		{
			AssertBool(harness.PreviewDoor.IsOpen).IsFalse(); AssertBool(harness.PreviewDoor.IsDiscovered).IsTrue();
			AssertBool(harness.PreviewDoor.Secret.IsSecret).IsFalse();
			AssertBool(harness.PreviewDoor.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
				.Any(m => VisualHelper.IsHelper(m) && m.Visible == doorFlag)).IsTrue();
		}
		else
		{
			AssertBool(harness.PreviewChest.IsOpen).IsFalse(); AssertObject(harness.PreviewChest.Xray).IsNotNull();
			AssertBool(harness.PreviewChest.Xray.IsEnabled).IsTrue();
			AssertInt(harness.PreviewChest.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
				.Count(m => VisualHelper.IsHelper(m) && m.Visible == lootFlag)).IsEqual(2);
		}
		harness.Replay(); AssertBool(harness.PreviewOccluder.Visible).IsFalse();
		harness.Replay(); AssertBool(harness.PreviewOccluder.Visible).IsTrue();
		AssertBool(GameDebug.DoorXrayEnabled == doorFlag && GameDebug.LootXrayEnabled == lootFlag).IsTrue();
		AssertBool(harness.PreviewDoor?.IsOpen == true || harness.PreviewChest?.IsOpen == true).IsFalse();
		harness.SelectEffect(0); await runner.SimulateFrames(2);
		AssertObject(harness.PreviewOccluder).IsNull(); AssertObject(harness.PreviewChest).IsNull();
		AssertBool(harness.PreviewTrap.IsDiscovered).IsFalse();
	}
}