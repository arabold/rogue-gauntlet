namespace RogueGauntlet.Tests;

using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Physical circulation, coherent surface choices and independently editable assemblies share the production pipeline.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomCompositionTest
{
	private static T Load<T>(string kind) where T : RoomGenerator => TestFactories.Load<T>(
		$"res://scenes/levels/garrison/rooms/{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);

	[TestCase("barracks")]
	[TestCase("cellar")]
	[TestCase("library")]
	public void RoomChoosesOneCoherentFloorAndWallFamily(string kind)
	{
		var recipe = Load<RoomGenerator>(kind);
		if (kind == "barracks") AssertBool(recipe.ShellBuilder.SurfaceStyles.All(style => style.Floor is not RoomMaterial.WoodPlanks and not RoomMaterial.StoneAndWood and not RoomMaterial.Earthen)).IsTrue();
		if (kind == "library") AssertBool(recipe.ShellBuilder.SurfaceStyles.Any(style => style.Floor == RoomMaterial.WoodPlanks && style.Weight < 2)).IsTrue();
		for (ulong seed = 1; seed <= 4; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = recipe.BuildRoom(rng);
			try
			{
				var style = recipe.ShellBuilder.SurfaceStyles.Single(style => style.Id == room.GetMeta("room_surface").AsString());
				var wallNames = style.StraightWalls.Append(style.HalfWall).Append(style.CornerWall).ToArray();
				AssertBool(room.WallGridMap.GetUsedCells().All(cell => wallNames.Contains(room.WallGridMap.MeshLibrary.GetItemName(room.WallGridMap.GetCellItem(cell))))).IsTrue();
				var floors = room.FloorGridMap.GetUsedCells().Select(cell => room.FloorGridMap.MeshLibrary.GetItemName(room.FloorGridMap.GetCellItem(cell))).Where(name => !name.EndsWith("_corner")).ToArray();
				if (style.Floor == RoomMaterial.LightWood) AssertBool(floors.All(name => name.StartsWith("floor_wood") && !name.EndsWith("dark"))).IsTrue();
				if (style.Floor == RoomMaterial.DarkWood) AssertBool(floors.All(name => name.StartsWith("floor_wood") && name.EndsWith("dark"))).IsTrue();
				if (style.Floor == RoomMaterial.Earthen) AssertBool(floors.All(name => name.StartsWith("floor_dirt"))).IsTrue();
				if (style.Floor == RoomMaterial.StoneSlab) AssertBool(floors.All(name => name.StartsWith("floor_tile"))).IsTrue();
			}
			finally { room.Free(); }
		}
	}

	[TestCase("partitioned_store", 0)]
	[TestCase("partitioned_store", 1)]
	[TestCase("partitioned_store", 2)]
	[TestCase("fenced_walkway", 0)]
	[TestCase("fenced_walkway", 1)]
	[TestCase("fenced_walkway", 2)]
	public void BoundariesRemainAccessibleAndSurviveEveryRotation(string kind, int variant)
	{
		var recipe = Load<ModularRoomGenerator>(kind);
		recipe.Layouts = [recipe.Layouts[variant]];
		AssertArray(recipe.Validate()).IsEmpty();
		for (ulong seed = 23; seed <= 24; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = recipe.BuildRoom(rng);
			try
			{
				var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
				AssertBool(slots.Any(slot => slot.Definition.Id.Contains("fence") || slot.Definition.Id.Contains("half_wall"))).IsTrue();
				AssertInt(slots.Select(slot => slot.Id).Distinct().Count()).IsEqual(slots.Length);
				for (int turn = 0; turn < 4; turn++) { ModularRoomTest.AssertRequiredSlotsFit(room, recipe); room.Rotate(1); }
				ProceduralRoomBuilder.SetOwnersForPacking(room, room);
				using var scene = new PackedScene();
				AssertBool(scene.Pack(room) == Error.Ok).IsTrue();
				var restored = scene.Instantiate<Room>();
				try { restored.BakeTileMap(); ModularRoomTest.AssertRequiredSlotsFit(restored, recipe); }
				finally { restored.Free(); }
			}
			finally { room.Free(); }
		}
	}

	[TestCase]
	public void ASealedBayIsRejectedWhileItsSingleEntranceConnectsEveryFreeArea()
	{
		var recipe = Load<ModularRoomGenerator>("partitioned_store");
		var boundary = (RoomBoundaryLayout)((CompositeRoomLayout)recipe.Layouts[0].Contents).Steps[0];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			var baseLayout = recipe.Layouts[0].BaseLayout;
			baseLayout.MinimumSize = baseLayout.MaximumSize = new(4, 4);
			AssertBool(baseLayout.Populate(room, recipe.ShellBuilder, rng)).IsTrue();
			var context = new RoomFurnishingContext(room, 23, 1);
			var transform = new Transform3D(Basis.Identity, new Vector3(-1.5f, 0, -1.5f));
			AssertBool(RoomBoundaryPatterns.PlaceBay(context, boundary.Palette, transform, new(6, 6), 4, true, "bay")).IsTrue();
			AssertBool(context.IsConnected).IsTrue();
			int pieces = context.Placements.Count;
			var closed = context.Fork();
			AssertBool(RoomBoundaryPatterns.PlaceRun(closed, boundary.Palette, transform * new Vector3(0, 0, 6), transform * new Vector3(4, 0, 6), "seal")).IsTrue();
			AssertBool(closed.IsConnected).IsFalse();
			AssertInt(context.Placements.Count).IsEqual(pieces);
		}
		finally { room.Free(); }
	}

	[TestCase]
	public void JoinedShelfColumnsKeepAccessibleAislesBetweenRowsAndReachTheCorners()
	{
		var recipe = Load<LibraryRoomGenerator>("library");
		recipe.ColumnsPerBank = 2;
		recipe.StackRows = [5];
		recipe.StackRowSpacing = 3.8f;
		AssertArray(recipe.Validate()).IsEmpty();
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = recipe.BuildRoom(rng);
		try
		{
			var ids = recipe.StackArrangement.Variants.Select(entry => entry.Id).ToArray();
			var stacks = room.GetChildren().OfType<RoomFurnishingSlot>().Where(slot => ids.Contains(slot.Definition.Id)).ToArray();
			AssertInt(stacks.Length).IsEqual(20);
			AssertInt(room.GetChildren().OfType<RoomFurnishingSlot>().Count(slot => slot.Definition.Id == recipe.CornerBookcase.Id)).IsEqual(8);
			AssertBool(recipe.StackRowSpacing - recipe.StackArrangement.GetPlacementBounds().Size.Z >= recipe.MinimumAisleWidth).IsTrue();
			AssertBool(stacks.Any(a => stacks.Any(b => a != b && Mathf.Abs(a.Position.X - b.Position.X - 4) < .001f && Mathf.Abs(a.Position.Z - b.Position.Z) < .001f))).IsTrue();
			AssertBool(stacks.Any(a => stacks.Any(b => a != b && Mathf.Abs(a.Position.Z - b.Position.Z - 3.8f) < .001f && Mathf.Abs(a.Position.X - b.Position.X) < .001f))).IsTrue();
			for (int turn = 0; turn < 4; turn++) { ModularRoomTest.AssertRequiredSlotsFit(room, recipe); room.Rotate(1); }
		}
		finally { room.Free(); }
	}

	[TestCase]
	public void ShelfLayoutsRejectRowsWhoseBookFacesHaveNoWalkableAisle()
	{
		var recipe = Load<LibraryRoomGenerator>("library");
		recipe.StackRowSpacing = 1.3f;
		AssertBool(recipe.Validate().Any(error => error.Contains("aisle"))).IsTrue();
		var shelves = TestFactories.Load<ShelfRowsRoomLayout>("res://scenes/levels/garrison/layouts/contents/study_shelves.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		shelves.Rows = 2;
		shelves.RowAisleWidth = 0;
		AssertBool(shelves.Validate().Length > 0).IsTrue();
	}

	[TestCase]
	public void SupplyStationsComposeIndependentPrimaryAndCompanionSlots()
	{
		var recipe = Load<ModularRoomGenerator>("cellar");
		recipe.Layouts = [recipe.Layouts[2]];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = recipe.BuildRoom(rng);
		try
		{
			var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
			AssertBool(slots.Any(slot => slot.Id.EndsWith(":primary"))).IsTrue();
			AssertBool(slots.Any(slot => slot.Id.EndsWith(":table") || slot.Id.EndsWith(":supplies"))).IsTrue();
			ModularRoomTest.AssertRequiredSlotsFit(room, recipe);
		}
		finally { room.Free(); }
	}
}