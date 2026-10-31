namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The room harness previews every realm's room types, axis overrides, rotation and floors.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomGenerationHarnessTest
{
	private const string Scene = "res://scenes/debug/rooms/room_generation_harness.tscn";

	[TestCase]
	public async Task EveryRecipePreviewsAndRoomTypesFurnishThemselves()
	{
		using var runner = TestFactories.LoadScene(Scene);
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		AssertString(harness.LastError).IsEmpty();
		for (int index = 0; index < harness.Recipes.Count; index++)
		{
			harness.SelectRecipe(index);
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).IsEmpty();
			if (harness.Recipes[index] is RoomType type)
			{
				AssertObject(harness.PreviewRoom.Type).IsSame(type);
				AssertBool(harness.PreviewBlueprint.IsConnected).IsTrue();
			}
			AssertBool(harness.PreviewCamera.Size > harness.PreviewRoom.Bounds.Size.X).IsTrue();
		}
		// Other realms list their own factory's types; RoomAssemblyTest assembles each of them.
		for (int realm = 1; realm < harness.Generator.Realms.Length; realm++)
		{
			harness.SelectRealm(realm);
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).OverrideFailureMessage($"{harness.Realm.Id}: {harness.LastError}").IsEmpty();
			AssertObject(harness.PreviewRoom.Type).IsSame(harness.Factory.RoomTypes[0]);
		}
	}

	[TestCase]
	public async Task FloorAndWallOverridesNeverMoveFurnitureAndFurnishingLeavesStructureAlone()
	{
		using var runner = TestFactories.LoadScene(Scene);
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		int barracks = harness.Recipes.ToList().FindIndex(recipe => recipe is RoomType { Id: "garrison.barracks" });
		harness.SelectRecipe(barracks);
		harness.Seed = 99;
		harness.Regenerate();
		string props = Props(harness.PreviewRoom);
		string walls = Cells(harness.PreviewRoom.WallGridMap);
		harness.FloorOverride = 1;
		harness.Regenerate();
		AssertString(Props(harness.PreviewRoom)).IsEqual(props);
		AssertString(Cells(harness.PreviewRoom.WallGridMap)).IsEqual(walls);
		harness.Furnishings = false;
		harness.Regenerate();
		AssertInt(harness.PreviewRoom.GetNode("Props").GetChildCount()).IsEqual(0);
		AssertString(Cells(harness.PreviewRoom.WallGridMap)).IsEqual(walls);
		harness.Furnishings = true;
		harness.FloorOverride = RoomAssemblyOptions.Seeded;
		harness.Regenerate();
		var before = harness.PreviewRoom.Map;
		harness.RotatePreview(1);
		harness.Regenerate();
		AssertInt(harness.PreviewRoom.Map.Width).IsEqual(before.Height);
		harness.RotatePreview(3);
		harness.Regenerate();
		AssertString(Props(harness.PreviewRoom)).IsEqual(props);
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
		// A realm other than the first: the floor view builds the selected realm's rooms and corridors.
		harness.SelectRealm(1);
		harness.Regenerate();
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		AssertObject(harness.Generator.CurrentRealm).IsSame(harness.Realm);
		AssertObject(harness.PreviewRoom).IsNull();
		AssertBool(harness.Generator.Visible).IsTrue();
		AssertArray(LayoutConnectivityTest.FindConnectivityFailures(harness.Generator)).IsEmpty();
		harness.Mode = RoomPreviewMode.Room;
		harness.SelectRecipe(0);
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		AssertBool(harness.PreviewRoot.Visible).IsTrue();
		AssertBool(harness.Generator.Visible).IsFalse();
		await runner.SimulateFrames(1);
	}

	private static string Props(Room room) => string.Join("|", room.GetNode("Props").GetChildren().OfType<Node3D>()
		.Select(prop => $"{prop.GetMeta("prop_id")}:{prop.Transform.Origin.Snapped(Vector3.One * .001f)}"));

	private static string Cells(GridMap grid) => string.Join("|", grid.GetUsedCells().OrderBy(cell => cell.X).ThenBy(cell => cell.Z)
		.Select(cell => $"{cell}:{grid.GetCellItem(cell)}:{grid.GetCellItemOrientation(cell)}"));
}