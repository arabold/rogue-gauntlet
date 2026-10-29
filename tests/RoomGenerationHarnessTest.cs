namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The room harness exercises production recipes, UI controls, rotation and complete-floor generation.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomGenerationHarnessTest
{
	private const string Scene = "res://scenes/debug/rooms/room_generation_harness.tscn";

	[TestCase]
	public async Task EveryRecipePreviewsAndUiControlsPreserveStructureWhenFurnishingsChange()
	{
		using var runner = TestFactories.LoadScene(Scene);
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		AssertString(harness.LastError).IsEmpty();
		AssertInt(harness.Factory.RoomGenerators.Length).IsEqual(5);
		for (int index = 0; index < harness.Factory.RoomGenerators.Length; index++)
		{
			harness.SelectRecipe(index);
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).IsEmpty();
			AssertObject(harness.PreviewRoom.Generator).IsSame(harness.Factory.RoomGenerators[index]);
			AssertBool(harness.PreviewRoom.GetChildren().OfType<RoomArrangement>().Any()).IsTrue();
			AssertBool(harness.PreviewCamera.Size > harness.PreviewRoom.Bounds.Size.X).IsTrue();
		}
		var pickers = harness.Controls.GetChildren().OfType<HBoxContainer>()
			.SelectMany(row => row.GetChildren()).OfType<OptionButton>().ToArray();
		pickers[1].EmitSignal(OptionButton.SignalName.ItemSelected, 0);
		pickers[1].EmitSignal(OptionButton.SignalName.ItemSelected, 1);
		pickers[1].EmitSignal(OptionButton.SignalName.ItemSelected, 3);
		await runner.SimulateFrames(1);
		AssertBool(harness.PreviewRoom.Generator is BarracksRoomGenerator).IsTrue();
		var seed = harness.Controls.GetChildren().OfType<HBoxContainer>()
			.SelectMany(row => row.GetChildren()).OfType<SpinBox>().Single();
		seed.Value = 99;
		await runner.SimulateFrames(1);
		AssertBool(harness.Seed == 99).IsTrue();
		string baseline = Geometry(harness.PreviewRoom);
		var toggle = harness.Controls.GetChildren().OfType<CheckButton>().Single();
		toggle.ButtonPressed = false;
		await runner.SimulateFrames(1);
		AssertInt(harness.PreviewRoom.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(0);
		AssertString(Geometry(harness.PreviewRoom)).IsEqual(baseline);
		toggle.ButtonPressed = true;
		await runner.SimulateFrames(1);
		AssertBool(harness.PreviewRoom.GetChildren().OfType<RoomArrangement>().Any()).IsTrue();
		var before = harness.PreviewRoom.Map;
		harness.RotatePreview(1);
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		AssertInt(harness.PreviewRoom.Map.Width).IsEqual(before.Height);
		AssertInt(harness.PreviewRoom.Map.Height).IsEqual(before.Width);
		harness.RotatePreview(3);
		await runner.SimulateFrames(1);
		AssertString(Geometry(harness.PreviewRoom)).IsEqual(baseline);
		await runner.SimulateFrames(1);
	}

	[TestCase]
	public async Task FloorViewUsesProductionFactoryAndCanReturnToAnIndividualRoom()
	{
		using var runner = TestFactories.LoadScene(Scene);
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		harness.Mode = RoomPreviewMode.Floor;
		harness.Seed = 23;
		harness.Regenerate();
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		AssertObject(harness.PreviewRoom).IsNull();
		AssertBool(harness.Generator.Visible).IsTrue();
		AssertBool(harness.PreviewRoot.Visible).IsFalse();
		AssertInt(harness.Generator.ArrangementCount).IsGreater(0);
		AssertArray(LayoutConnectivityTest.FindConnectivityFailures(harness.Generator)).IsEmpty();
		string layout = string.Join("|", harness.Generator.Map.Tiles.Cast<MapTile>());
		harness.Furnishings = false;
		harness.Regenerate();
		await runner.SimulateFrames(1);
		AssertInt(harness.Generator.ArrangementCount).IsEqual(0);
		AssertString(string.Join("|", harness.Generator.Map.Tiles.Cast<MapTile>())).IsEqual(layout);
		harness.Mode = RoomPreviewMode.Room;
		harness.Furnishings = true;
		harness.SelectRecipe(4);
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		AssertBool(harness.PreviewRoom.Generator is LibraryRoomGenerator).IsTrue();
		AssertBool(harness.PreviewRoot.Visible).IsTrue();
		AssertBool(harness.Generator.Visible).IsFalse();
		await runner.SimulateFrames(1);
	}

	private static string Geometry(Room room) => string.Join("|", room.FloorGridMap.GetUsedCells().OrderBy(cell => cell.X)
		.ThenBy(cell => cell.Z).Select(cell => $"{cell}:{room.FloorGridMap.GetCellItem(cell)}:{room.FloorGridMap.GetCellItemOrientation(cell)}"));
}