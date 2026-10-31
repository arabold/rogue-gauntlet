namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Cell rows retain accessible gates through composition, rotation, packing and floor save reconstruction.</summary>
[TestSuite, RequireGodotRuntime]
public class PrisonRoomTest
{
	private const string Recipe = "res://scenes/levels/garrison/rooms/prison/garrison_prison_generator.tres";
	private static PrisonRoomGenerator Load() => TestFactories.Load<PrisonRoomGenerator>(Recipe, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
	private static Door[] Gates(Room room) => room.InteriorStructure.GetChildren().OfType<Door>().ToArray();

	[TestCase(1, 1)]
	[TestCase(1, 2)]
	[TestCase(2, 1)]
	[TestCase(2, 2)]
	public void CellRowsKeepEveryRequiredSlotAndGateThroughRotationAndPacking(int sides, int count)
	{
		var recipe = Load();
		recipe.Rows = (PrisonCellRows)sides; recipe.CellsPerWall = [count];
		AssertArray(recipe.Validate()).IsEmpty();
		foreach (ulong seed in new ulong[] { 23, 99 })
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = recipe.BuildRoom(rng);
			try
			{
				AssertInt(Gates(room).Length).IsEqual(sides * count);
				AssertInt(Gates(room).Select(gate => gate.Interior.Id).Distinct().Count()).IsEqual(sides * count);
				var walls = room.InteriorStructure.GetChildren().OfType<RoomArrangement>().ToArray();
				AssertBool(walls.Any(wall => wall.GetMeta("structural_item").AsString() == "wall_gated")).IsTrue();
				AssertBool(walls.Any(wall => wall.GetMeta("structural_item").AsString() == "wall_archedwindow_gated")).IsTrue();
				AssertInt(walls.Count(wall => wall.Name.ToString().Contains("divider"))).IsEqual(sides * (count + 1) * 2);
				AssertInt(room.GetChildren().OfType<RoomFurnishingSlot>().Count(slot => slot.Id.EndsWith(":bed"))).IsEqual(sides * count);
				for (int turn = 0; turn < 4; turn++) { ModularRoomTest.AssertRequiredSlotsFit(room, recipe); room.Rotate(1); }
				ProceduralRoomBuilder.SetOwnersForPacking(room, room);
				using var scene = new PackedScene();
				AssertBool(scene.Pack(room) == Error.Ok).IsTrue();
				var restored = scene.Instantiate<Room>();
				try
				{
					restored.BakeTileMap();
					AssertInt(Gates(restored).Length).IsEqual(sides * count);
					AssertBool(Gates(restored).All(gate => gate.Interior.Door == gate && gate.Leaf != null)).IsTrue();
					ModularRoomTest.AssertRequiredSlotsFit(restored, recipe);
				}
				finally { restored.Free(); }
			}
			finally { room.Free(); }
		}
	}

	[TestCase]
	public void OptionalGuardAndSupplyAssembliesFitWhileUnfurnishedCellsKeepTheirStructure()
	{
		var recipe = Load(); recipe.Rows = PrisonCellRows.OpposingWalls; recipe.CellsPerWall = [2];
		recipe.GuardStationChance = recipe.SuppliesChance = 1;
		bool table = false, chair = false, barrels = false;
		for (ulong seed = 1; seed <= 6; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = new Room();
			try
			{
				AssertBool(recipe.PopulateRoom(room, rng, furnish: false)).IsTrue();
				AssertInt(Gates(room).Length).IsEqual(4);
				AssertInt(room.GetChildren().OfType<RoomArrangement>().Count()).IsEqual(0);
				var ids = room.GetChildren().OfType<RoomFurnishingSlot>().Select(slot => slot.Id).ToArray();
				table |= ids.Any(id => id == "prison:guard:primary");
				chair |= ids.Any(id => id == "prison:guard:chair" || id == "prison:guard:second_chair");
				barrels |= ids.Any(id => id == "prison:supplies:primary");
				ModularRoomTest.AssertRequiredSlotsFit(room, recipe);
			}
			finally { room.Free(); }
		}
		AssertBool(table).IsTrue(); AssertBool(chair).IsTrue(); AssertBool(barrels).IsTrue();
	}

	[TestCase]
	public async Task CellGateUsesProductionInteractionCollisionAndNavigation()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/rooms/room_generation_harness.tscn");
		await runner.SimulateFrames(2);
		var gate = TestFactories.Load<PackedScene>("res://scenes/props/prison/cell_gate.tscn").Instantiate<Door>();
		gate.Interior.Id = "test:0"; gate.Position = new(1000, 0, 0);
		TestFactories.AddToSceneTree(gate);
		var actor = TestFactories.AddToSceneTree(new CharacterBody3D { Position = new(1000, 0, 1), CollisionLayer = 4, CollisionMask = 251 });
		actor.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 1 }, Position = Vector3.Up });
		var from = new Transform3D(Basis.Identity, gate.Position + new Vector3(0, 1, 1.8f));
		await runner.SimulateFrames(2, 20);
		AssertBool(actor.TestMove(from, new Vector3(0, 0, -3.6f))).IsTrue();
		var execution = new ActorInteractionComponent { Actor = actor }; actor.AddChild(execution);
		var context = new ActorInteractionContext(actor, new ActorCapabilities { CanOpenDoors = true }, new Inventory());
		AssertBool(execution.TryBegin(gate.Interactive.GetOffer(context), context) == InteractionRequestResult.Success).IsTrue();
		execution.CancelCurrent();
		AssertBool(gate.IsOpen).IsFalse();
		AssertBool(execution.TryBegin(gate.Interactive.GetOffer(context), context) == InteractionRequestResult.Success).IsTrue();
		execution._PhysicsProcess(.6);
		await runner.SimulateFrames(30, 20);
		AssertBool(gate.IsOpen && gate.Collision.Disabled && gate.NavigationLink.Enabled).IsTrue();
		AssertBool(actor.TestMove(from, new Vector3(0, 0, -3.6f))).IsFalse();
		AssertBool(execution.TryBegin(gate.Interactive.GetOffer(context), context) == InteractionRequestResult.Success).IsTrue();
		execution._PhysicsProcess(1.1);
		await runner.SimulateFrames(30, 20);
		AssertBool(!gate.IsOpen && !gate.Collision.Disabled && !gate.NavigationLink.Enabled).IsTrue();
	}

	[TestCase]
	public async Task InteriorGatesKeepIndependentIdsAndRestoreAfterFloorReconstruction()
	{
		var recipe = Load(); recipe.Rows = PrisonCellRows.OpposingWalls; recipe.CellsPerWall = [2];
		var factory = TestFactories.Load<MixedRoomFactory>("res://scenes/levels/garrison/factories/garrison_mixed_room_factory.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		factory.RoomGenerators = [recipe];
		var (runner, level) = await TestFactories.LoadLevel(new PackedRoomLayout(), factory);
		using (runner)
		{
			TestFactories.WireLootScenes(level.MapGenerator);
			level.MapGenerator.MinChests = level.MapGenerator.MaxChests = 4;
			level.MapGenerator.MapWidth = level.MapGenerator.MapDepth = 30; level.MapGenerator.MaxRooms = 4;
			level.MapGenerator.Seed = 23;
			level.Generate();
			var floor = level.Floor;
			var gates = floor.DoorBaselines.Values.Select(pair => pair.Door).Where(door => door.Interior != null).ToArray();
			AssertInt(gates.Length).IsGreater(0);
			AssertBool(gates.All(gate => !floor.Doors.Any(pair => pair.Door == gate) && gate.ConnectionId.Contains(":interior:") && gate.Lock == null)).IsTrue();
			AssertInt(gates.Select(gate => gate.ConnectionId).Distinct().Count()).IsEqual(gates.Length);
			var clearances = InteriorDoorComponent.CollectClearances(level.MapGenerator.NavigationRegion, Transform3D.Identity);
			var chests = level.MapGenerator.NavigationRegion.GetChildren().OfType<Chest>().ToArray();
			AssertInt(chests.Length).IsGreater(0);
			AssertBool(chests.SelectMany(chest => RoomArrangement.GetStaticPropBounds(chest, chest.Transform))
				.All(box => clearances.All(clearance => !box.Intersects(clearance)))).IsTrue();
			string id = gates[0].ConnectionId;
			gates[0].IsOpen = true;
			var saved = level.CaptureExploration();
			AssertBool(saved.Doors.Any(door => door.ConnectionId == id && door.IsOpen)).IsTrue();
			level.Generate();
			level.RestoreExploration(saved);
			var restored = level.Floor.DoorBaselines[id].Door;
			AssertBool(restored.IsOpen && restored.Collision.Disabled && restored.NavigationLink.Enabled).IsTrue();
			await runner.SimulateFrames(1);
		}
	}

	[TestCase]
	public async Task PrisonPreviewOverridesDoNotMutateTheProductionRecipe()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/rooms/room_generation_harness.tscn");
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		harness.SelectRecipe(9);
		var source = (PrisonRoomGenerator)harness.Factory.RoomGenerators[9];
		foreach (int variant in new[] { 0, 1 })
		{
			harness.SelectedLayoutVariant = variant;
			harness.Regenerate();
			AssertString(harness.LastError).IsEmpty();
			AssertString(harness.PreviewRoom.GetMeta("room_layout_variant").AsString()).IsEqual(variant == 0 ? "prison.one_wall" : "prison.opposing_walls");
		}
		AssertBool(source.Rows == PrisonCellRows.Random).IsTrue();
		await runner.SimulateFrames(1);
	}
}