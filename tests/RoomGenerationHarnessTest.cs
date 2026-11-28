namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>The room harness plays every realm's room types and groups, with their axis overrides and rotation.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomGenerationHarnessTest
{
	public const string Scene = "res://scenes/debug/rooms/room_generation_harness.tscn";

	/// <summary>The harness after its first frames, with the hero's device input off so tests steer them.</summary>
	public static async Task<RoomGenerationHarness> Settle(ISceneRunner runner)
	{
		await TestFactories.AwaitPhysicsFrames(runner, 2);
		var harness = runner.Scene().GetNode<RoomGenerationHarness>("Harness");
		harness.Player.InputController.SetPhysicsProcess(false);
		return harness;
	}

	/// <summary>Every room and group stands with the hero waiting at its entrance, on floor monsters can navigate.</summary>
	[TestCase]
	public async Task EveryRecipeStandsWithTheHeroAtItsEntrance()
	{
		using var runner = TestFactories.LoadScene(Scene);
		var harness = await Settle(runner);
		AssertString(harness.LastError).IsEmpty();
		for (int index = 0; index < harness.Recipes.Count; index++)
		{
			harness.SelectRecipe(index);
			harness.Regenerate();
			AssertRecipePlays(harness);
		}
		// Other realms list their own pool's rooms; RoomAssemblyTest assembles each of them.
		for (int realm = 1; realm < harness.Realms.Count; realm++)
		{
			harness.SelectRealm(realm);
			harness.Regenerate();
			AssertRecipePlays(harness);
			AssertObject(harness.PreviewRoom.Type).IsSame(harness.Pool.Rooms[0]);
		}
		await TestFactories.AwaitPhysicsFrames(runner, 1);
	}

	private static void AssertRecipePlays(RoomGenerationHarness harness)
	{
		string at = $"{harness.Realm.Id} {harness.Recipes[harness.SelectedRecipe].Id}";
		AssertString(harness.LastError).OverrideFailureMessage($"{at}: {harness.LastError}").IsEmpty();
		if (harness.Recipes[harness.SelectedRecipe] is RoomType type)
		{
			AssertObject(harness.PreviewRoom.Type).IsSame(type);
			AssertBool(harness.PreviewBlueprint.IsConnected).OverrideFailureMessage($"{at}: connected").IsTrue();
		}
		AssertFloat(harness.Player.GlobalPosition.HorizontalDistanceTo(harness.Entrance)).OverrideFailureMessage($"{at}: hero at the entrance").IsLess(.01f);
		AssertBool(harness.Arena.NavigationFor(NavigationLayer.Ground).IsTraversable(harness.Entrance, .5f))
			.OverrideFailureMessage($"{at}: the entrance is open floor").IsTrue();
	}

	/// <summary>
	/// The harness's floor, furnishing and rotation controls reach the staged room; that surfaces never move furniture is
	/// an assembly contract (<see cref="RoomAssemblyTest.FloorStylesNeverMoveFurnitureAndFurnishingLeavesWallsAlone"/>).
	/// </summary>
	[TestCase]
	public async Task FloorFurnishingAndRotationControlsReachTheRoom()
	{
		using var runner = TestFactories.LoadScene(Scene);
		var harness = await Settle(runner);
		int barracks = harness.Recipes.ToList().FindIndex(recipe => recipe is RoomType { Id: "barracks" });
		harness.SelectRecipe(barracks);
		harness.Seed = 99;
		// Columns are structure, not furnishing: keep them out so a bare room has no props at all.
		harness.ColumnsOverride = RoomAssemblyOptions.Off;
		harness.FloorOverride = 0;
		harness.Regenerate();
		string props = Props(harness.PreviewRoom);
		string floor = Cells(harness.PreviewRoom.FloorGridMap);
		harness.FloorOverride = 1;
		harness.Regenerate();
		AssertString(Cells(harness.PreviewRoom.FloorGridMap)).IsNotEqual(floor);
		harness.Furnishings = false;
		harness.Regenerate();
		AssertInt(harness.PreviewRoom.GetNode("Props").GetChildCount()).IsEqual(0);
		harness.Furnishings = true;
		harness.FloorOverride = 0;
		harness.Regenerate();
		var before = harness.PreviewRoom.Map;
		harness.RotatePreview(1);
		harness.Regenerate();
		AssertInt(harness.PreviewRoom.Map.Width).IsEqual(before.Height);
		harness.RotatePreview(3);
		harness.Regenerate();
		AssertString(Props(harness.PreviewRoom)).IsEqual(props);
		await TestFactories.AwaitPhysicsFrames(runner, 1);
	}

	private static string Props(Room room) => string.Join("|", room.GetNode("Props").GetChildren().OfType<Node3D>()
		.Select(prop => $"{prop.GetMeta("prop_id")}:{prop.Transform.Origin.Snapped(Vector3.One * .001f)}"));

	private static string Cells(GridMap grid) => string.Join("|", grid.GetUsedCells().OrderBy(cell => cell.X).ThenBy(cell => cell.Z)
		.Select(cell => $"{cell}:{grid.GetCellItem(cell)}:{grid.GetCellItemOrientation(cell)}"));
}