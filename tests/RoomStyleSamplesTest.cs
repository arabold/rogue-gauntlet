namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Depot and treasury rooms retain their layouts, defining content and gold rewards.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomStyleSamplesTest
{
	private const string RoomTypes = "res://scenes/levels/rooms/room_types/";

	[TestCase("partitioned_depot")]
	[TestCase("treasury")]
	public void SamplesReproduceConnectedLayoutsAndTheirDefiningContent(string name)
	{
		var type = TestFactories.Load<RoomType>($"{RoomTypes}{name}.tres");
		AssertArray(type.Validate()).IsEmpty();
		foreach (ulong seed in new ulong[] { 1, 23, 41, 99, 7919, 104729 })
		{
			var first = RoomAssembler.Build(type, seed);
			var second = RoomAssembler.Build(type, seed);
			try
			{
				AssertBool(first.IsAcceptable).OverrideFailureMessage($"{name} seed {seed}").IsTrue();
				AssertString(Signature(second)).IsEqual(Signature(first));
				int maximumTiles = name == "treasury" ? 9 : 20;
				AssertInt(first.Footprint.Count).IsLessEqual(maximumTiles);
				AssertString(RoomAssemblyTest.FloorCoverage(first)).IsEmpty();
				AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "lit_sconce")).IsGreaterEqual(2);
				var nodes = first.Room.FindChildren("*", "", true, false);
				if (name == "treasury")
				{
					var chest = nodes.OfType<Chest>().Single();
					AssertString(chest.LootTable.Profile.ResourcePath).IsEqual("res://scenes/items/loot/loot_treasury_gold.tres");
				}
			}
			finally
			{
				first.Room.Free();
				second.Room.Free();
			}
		}
	}

	/// <summary>Junctions replace perimeter pieces and their outward-facing ends join without overlapping other walls.</summary>
	[TestCase(0, 0)]
	[TestCase(1, 1)]
	[TestCase(2, 2)]
	public void DepotWallVariantsJoinWithoutOverlapping(int style, int sides)
	{
		var type = TestFactories.Load<RoomType>($"{RoomTypes}partitioned_depot.tres");
		var ends = new System.Collections.Generic.HashSet<string>();
		foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8, 31, 60, 65, 95 })
		{
			var blueprint = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Walls = style });
			try
			{
				AssertBool(blueprint.IsAcceptable).OverrideFailureMessage($"style {style} seed {seed}").IsTrue();
				AssertString(RoomAssemblyTest.FloorCoverage(blueprint)).IsEmpty();
				var grid = blueprint.Room.WallGridMap;
				var cells = grid.GetUsedCells().Cast<Vector3I>().ToArray();
				string Name(Vector3I cell) => grid.MeshLibrary.GetItemName(grid.GetCellItem(cell));
				var junctions = cells.Where(cell => Name(cell) == "wall_Tsplit").ToArray();
				var wallBoxes = WallFootprints.Collect(grid);
				AssertString(WallFootprints.FindOverlap(grid)).IsEmpty();
				AssertInt(junctions.Length).IsEqual(sides);
				AssertInt(blueprint.Pieces.Count(piece => piece.Prop.Id == "partition_wall")).IsEqual(sides == 0 ? 3 : 0);
				foreach (var junction in junctions)
				{
					var direction = junction.X < blueprint.Interior.GetCenter().X ? Vector3I.Right : Vector3I.Left;
					AssertBool(wallBoxes.Any(box => Mathf.Abs(box.Size.X - 2.5f) < .05f && Mathf.Abs(box.Size.Z - 1f) < .05f
						&& Mathf.Abs(box.GetCenter().Z - junction.Z) < .05f && Mathf.Abs(box.GetCenter().X - junction.X) < 1f)).IsTrue();
					var end = junction + direction * Room.TileSize;
					AssertBool(cells.Contains(end)).IsTrue();
					string name = Name(end);
					AssertBool(name is "wall_half_endcap" or "wall_half_endcap_sloped").IsTrue();
					ends.Add(name);
					int facing = grid.GetCellItemOrientation(junction);
					int turn = Array.IndexOf(GridTurns.Upright, facing);
					AssertInt(grid.GetCellItemOrientation(end)).IsEqual(GridTurns.Upright[(turn + 3) % 4]);
				}
				if (sides > 0)
				{
					var footprint = blueprint.Footprint;
					BoundaryWallBuilder.Build(grid,
						footprint.Tiles.Select(tile => (tile, (Vector3I)RoomFootprint.TileCenter(tile))),
						(tile, dx, dz) => !footprint.IsFloor(tile + new Vector2I(dx, dz)) && !blueprint.IsDoorway(tile, new Vector2I(dx, dz)),
						dividerCells: blueprint.Room.DividerCells, framedEdges: blueprint.Room.FramedEdges);
					AssertInt(grid.GetUsedCells().Cast<Vector3I>().Count(cell => Name(cell) == "wall_Tsplit")).IsEqual(sides);
					AssertInt(grid.GetUsedCells().Cast<Vector3I>().Count(cell => Name(cell) is "wall_half_endcap" or "wall_half_endcap_sloped")).IsEqual(sides);
					for (int turn = 0; turn < 4; turn++)
					{
						var before = blueprint.Room.DividerCells.ToArray();
						blueprint.Room.Rotate(1);
						foreach (var cell in before)
						{
							var rotated = new Vector3I(cell.Z, cell.Y, -cell.X);
							AssertBool(blueprint.Room.DividerCells.Contains(rotated)).IsTrue();
							AssertInt(grid.GetCellItem(rotated)).IsGreaterEqual(0);
						}
					}
				}
			}
			finally { blueprint.Room.Free(); }
		}
		if (sides > 0) AssertInt(ends.Count).IsEqual(2);
	}

	[TestCase]
	public void DividerRetriesAnUnsuitableShape()
	{
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}partitioned_depot.tres").Duplicate();
		type.Shapes = [new RectangleShape { MinimumSize = new Vector2I(2, 2), MaximumSize = new Vector2I(3, 3) }];
		type.Walls = [TestFactories.Load<DividerWallStyle>("res://scenes/levels/rooms/styles/walls/divider_walls_one.tres")];
		type.MinDoorways = type.MaxDoorways = 1;
		type.DoorwayPlacement = DoorwayPlacement.SingleEnd;
		type.DoorChance = type.FrameChance = 0;
		type.Steps = [];
		bool retried = false;
		for (ulong seed = 1; seed <= 40 && !retried; seed++)
		{
			type.Attempts = 1;
			try { var first = RoomAssembler.Build(type, seed); first.Room.Free(); }
			catch (InvalidOperationException error)
			{
				if (!error.Message.Contains("wall style")) continue;
				type.Attempts = 8;
				try
				{
					var next = RoomAssembler.Build(type, seed);
					retried = next.IsAcceptable;
					next.Room.Free();
				}
				catch (InvalidOperationException) { }
			}
		}
		AssertBool(retried).IsTrue();
	}

	[TestCase(0)] // ordinary door
	[TestCase(1)] // secret door
	[TestCase(2)] // doorless frame
	[TestCase(3)] // bare opening
	public void DoorwayFramesKeepTheirOpeningsClear(int mode)
	{
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres").Duplicate();
		type.SecretDoorChance = mode == 1 ? 1 : 0;
		type.DoorChance = mode < 2 ? 1 : 0;
		type.FrameChance = mode == 2 ? 1 : 0;
		var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
		try
		{
			var room = blueprint.Room;
			AssertInt(room.FramedEdges.Count).IsEqual(mode == 3 ? 0 : 1);
			// The door leaf owns its own collision; the frame adds no permanent obstruction.
			AssertInt(room.GetChildren().OfType<StaticBody3D>().Count()).IsEqual(0);
			AssertBool(blueprint.IsAcceptable).IsTrue();
		}
		finally { blueprint.Room.Free(); }
	}

	/// <summary>The player-sized body can cross open frames and doors in every room rotation; a closed leaf still blocks it.</summary>
	[TestCase(false)]
	[TestCase(true)]
	public async Task PlayerFitsThroughDoorway(bool hasDoor)
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/rooms/room_generation_harness.tscn");
		await runner.SimulateFrames(2, 20);
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres").Duplicate();
		type.SecretDoorChance = 0;
		type.DoorChance = hasDoor ? 1 : 0;
		type.FrameChance = 1;
		for (int turn = 0; turn < 4; turn++)
		{
			var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
			var room = blueprint.Room;
			var doorway = blueprint.Doorways.Single();
			var rotation = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
			room.Rotate(turn);
			room.Position = new Vector3(1000, 0, 1000);
			runner.Scene().AddChild(room);
			await runner.SimulateFrames(2, 20);
			var outward = rotation * doorway.Outward;
			var center = room.GlobalTransform * (rotation * doorway.Edge);
			using var query = new PhysicsShapeQueryParameters3D
			{
				Shape = new SphereShape3D { Radius = 1f },
				Transform = new Transform3D(Basis.Identity, center - outward * 3 + Vector3.Up),
				Motion = outward * 6,
				CollisionMask = 18,
			};
			var space = room.GetWorld3D().DirectSpaceState;
			if (hasDoor)
			{
				AssertFloat(space.CastMotion(query)[0]).IsLess(1f);
				room.Doors.Values.Single().IsOpen = true;
				await runner.SimulateFrames(2, 20);
			}
			AssertFloat(space.CastMotion(query)[0]).OverrideFailureMessage($"door={hasDoor}, rotation={turn}").IsGreaterEqual(.99f);
			room.QueueFree();
			await runner.SimulateFrames(1, 20);
		}
	}

	/// <summary>A room's secret chance owns the doorway decision; the wall and discovery state follow the door.</summary>
	[TestCase(0, false)]
	[TestCase(1, true)]
	public async Task TreasuryChoosesItsOwnSecretDoor(int chance, bool expectedSecret)
	{
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres").Duplicate();
		type.SecretDoorChance = chance;
		var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
		try
		{
			AssertInt(blueprint.Room.Doors.Count).IsEqual(1);
			var door = blueprint.Room.Doors.Values.Single();
			AssertBool(door.Secret.IsSecret).IsEqual(expectedSecret);
			if (!expectedSecret) return;
			AssertBool(door.IsDiscovered).IsFalse();
			var wall = door.GetChildren().OfType<GridMap>().Single();
			int item = wall.GetCellItem(Vector3I.Zero);
			AssertString(wall.MeshLibrary.GetItemName(item)).IsEqual("wall");
			AssertFloat(wall.MeshLibrary.GetItemMesh(item).GetAabb().Size.X).IsEqual(Room.TileSize);
			var roomWalls = blueprint.Room.WallGridMap;
			int orientation = roomWalls.GetOrthogonalIndexFromBasis(door.Transform.Basis);
			foreach (Vector3I cell in roomWalls.GetUsedCells())
				AssertBool(WallFootprints.PiecesOverlap(roomWalls.MeshLibrary, item, orientation, (Vector3I)door.Position,
					roomWalls.GetCellItem(cell), roomWalls.GetCellItemOrientation(cell), cell)).IsFalse();
			TestFactories.AddToSceneTree(blueprint.Room);
			AssertBool(door.DoorVisual.Visible).IsFalse();
			AssertInt((int)wall.CollisionLayer).IsEqual(2);
			for (int turn = 0; turn < 4; turn++)
			{
				var outward = door.GlobalBasis * Vector3.Forward;
				AssertBool(door.Secret.CanBeObservedFrom(door.GlobalPosition + outward * 3)).IsTrue();
				AssertBool(door.Secret.CanBeObservedFrom(door.GlobalPosition - outward * 3)).IsTrue();
				blueprint.Room.Rotate(1);
			}
			AssertBool(door.Secret.Reveal()).IsTrue();
			AssertBool(door.IsDiscovered).IsTrue();
			AssertBool(door.Secret.DiscoveryEffect.IsPlaying).IsTrue();
			AssertBool(door.IsOpen).IsFalse();
			AssertInt(wall.GetCellItem(Vector3I.Zero)).IsEqual(-1);
			door.Secret.Restore(false);
			AssertInt(wall.GetCellItem(Vector3I.Zero)).IsEqual(item);
			await blueprint.Room.ToSignal(blueprint.Room.GetTree(), SceneTree.SignalName.ProcessFrame);
			await blueprint.Room.ToSignal(blueprint.Room.GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		finally { blueprint.Room.Free(); }
	}

	[TestCase]
	public void TreasurySecretChoiceIsSeededAndIncludesBothDoorKinds()
	{
		var type = TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres");
		int secrets = 0;
		for (ulong seed = 1; seed <= 20; seed++)
		{
			var first = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Furnish = false });
			var second = RoomAssembler.Build(type, seed, new RoomAssemblyOptions { Furnish = false });
			try
			{
				bool secret = first.Room.Doors.Values.Single().Secret.IsSecret;
				AssertBool(second.Room.Doors.Values.Single().Secret.IsSecret).IsEqual(secret);
				if (secret) secrets++;
			}
			finally { first.Room.Free(); second.Room.Free(); }
		}
		AssertInt(secrets).IsGreater(0);
		AssertInt(secrets).IsLess(20);
	}

	[TestCase]
	public void RequiredGroupMemberRollsBackTheAnchorWhenItCannotFit()
	{
		var type = TestFactories.Load<RoomType>($"{RoomTypes}partitioned_depot.tres");
		var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
		try
		{
			var wall = TestFactories.Load<Prop>("res://scenes/levels/rooms/props/structure/partition_wall.tres");
			using var member = new PropGroupMember { Options = [wall], Offset = Vector3.Right * 100, Required = true };
			using var group = new PropGroup { Id = "test.required_group", Anchor = [wall], Members = [member], MirrorChance = 0 };
			using var rng = blueprint.Random("required_group");
			var at = new Transform3D(Basis.Identity, new Vector3(8, 0, 8));
			string before = Convert.ToBase64String(blueprint.Grid.Save());
			AssertBool(blueprint.TryPlace(group.Compose(blueprint, rng), at)).IsFalse();
			AssertInt(blueprint.Pieces.Count).IsEqual(0);
			AssertString(Convert.ToBase64String(blueprint.Grid.Save())).IsEqual(before);
			member.Required = false;
			AssertBool(blueprint.TryPlace(group.Compose(blueprint, rng), at)).IsTrue();
			AssertInt(blueprint.Pieces.Count).IsEqual(1);
		}
		finally { blueprint.Room.Free(); }
	}

	[TestCase]
	public void TreasuryChestGuaranteesAuthoredGold()
	{
		var profile = TestFactories.Load<LootDropProfile>("res://scenes/items/loot/loot_treasury_gold.tres");
		AssertArray(profile.Table.Validate()).IsEmpty();
		foreach (uint depth in new uint[] { 1, 3, 9 })
		{
			using var rng = GenerationRandom.Create(23, GenerationRandom.Loot, "test.treasury");
			var rewards = LootTableComponent.GenerateOutcome(profile, depth, rng, "test.treasury");
			AssertInt(rewards.Count).IsEqual(1);
			AssertBool(rewards[0].Item is Gold).IsTrue();
			AssertInt(rewards[0].Quantity).IsEqual(240);
		}
	}

	[TestCase]
	public async Task HarnessPreviewsProductionDepotPrisonAndTreasury()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/rooms/room_generation_harness.tscn");
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		var pool = (AssembledRoomFactory)harness.Generator.Rooms;
		foreach (string id in new[] { "partitioned_depot", "prison", "treasury" })
		{
			var type = pool.RoomTypes.Single(room => room.Id == id);
			AssertBool(type.Realms.Contains("garrison")).IsTrue();
			int index = harness.Recipes.ToList().FindIndex(room => room == type);
			AssertInt(index).IsGreaterEqual(0);
			harness.SelectRecipe(index);
			harness.Regenerate();
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).IsEmpty();
			AssertObject(harness.PreviewRoom.Type).IsSame(type);
			AssertBool(harness.PreviewBlueprint.IsAcceptable).IsTrue();
		}
	}

	private static string Signature(RoomBlueprint blueprint) => string.Join("|", blueprint.Pieces.Select(piece =>
		$"{piece.Prop.Id}:{piece.Variant}:{piece.Transform}"));
}