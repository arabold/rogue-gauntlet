namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Depot dividers, treasury doors and room lighting: the style choices a room type makes, and that doorways stay passable.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomStyleSamplesTest
{
	private const string RoomTypes = "res://scenes/levels/rooms/room_types/";

	[TestCase]
	public void RoomLightingIsSeededAndIndependentOfPlacement()
	{
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres").Duplicate();
		string signature = null;
		bool[] previous = null;
		foreach (float chance in new[] { 0f, 1f, .5f, .5f })
		{
			type.FixtureLitChance = chance;
			var blueprint = RoomAssembler.Build(type, 23);
			try
			{
				var fixtures = blueprint.Room.FindChildren("*", "", true, false).OfType<LightFixture>().ToArray();
				AssertInt(fixtures.Length).IsGreater(0);
				if (chance == 0) AssertBool(fixtures.All(fixture => !fixture.IsLit)).IsTrue();
				if (chance == 1) AssertBool(fixtures.All(fixture => fixture.IsLit)).IsTrue();
				if (chance == .5f && previous != null)
					AssertBool(fixtures.Select(fixture => fixture.IsLit).SequenceEqual(previous)).IsTrue();
				if (chance == .5f) previous = fixtures.Select(fixture => fixture.IsLit).ToArray();
				if (signature != null) AssertString(Signature(blueprint)).IsEqual(signature);
				signature = Signature(blueprint);
			}
			finally { blueprint.Room.Free(); }
		}
	}

	/// <summary>Junctions replace perimeter pieces and their outward-facing ends join without overlapping other walls.</summary>
	[TestCase]
	public void DepotWallVariantsJoinWithoutOverlapping()
	{
		var type = TestFactories.Load<RoomType>($"{RoomTypes}partitioned_depot.tres");
		// Wall style index and the divider junctions it builds: free-standing partitions, then one or two divider walls.
		foreach (var (style, sides) in new[] { (0, 0), (1, 1), (2, 2) }) AssertDepotWalls(type, style, sides);
	}

	private static void AssertDepotWalls(RoomType type, int style, int sides)
	{
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

	/// <summary>
	/// The player's body crosses every kind of doorway in every room rotation: an open door, a revealed and opened secret
	/// door, a doorless frame and a bare opening. A closed leaf or a concealed wall blocks it.
	/// </summary>
	[TestCase]
	public async Task PlayerFitsThroughEveryKindOfDoorway()
	{
		var player = TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<CharacterBody3D>();
		var playerBody = player.GetNode<CollisionShape3D>("CollisionShape3D");
		var (bodyShape, bodyOffset) = (playerBody.Shape, playerBody.Position);
		player.Free();
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres").Duplicate();
		var root = TestFactories.AddToSceneTree(new Node3D());
		var tree = root.GetTree();
		async Task PhysicsFrames(int count)
		{
			for (int frame = 0; frame < count; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
		}
		foreach (var (kind, door, secret, frame) in new[] { ("door", 1f, 0f, 0f), ("secret door", 1f, 1f, 0f), ("frame", 0f, 0f, 1f), ("opening", 0f, 0f, 0f) })
		{
			type.DoorChance = door;
			type.SecretDoorChance = secret;
			type.FrameChance = frame;
			for (int turn = 0; turn < 4; turn++)
			{
				var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
				var room = blueprint.Room;
				var doorway = blueprint.Doorways.Single();
				var rotation = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
				room.Rotate(turn);
				room.Position = new Vector3(1000, 0, 1000);
				root.AddChild(room);
				await PhysicsFrames(2);
				var outward = rotation * doorway.Outward;
				var center = room.GlobalTransform * (rotation * doorway.Edge);
				using var query = new PhysicsShapeQueryParameters3D
				{
					Shape = bodyShape,
					Transform = new Transform3D(Basis.Identity, center - outward * 3 + bodyOffset),
					Motion = outward * 6,
					CollisionMask = 18,
				};
				var space = room.GetWorld3D().DirectSpaceState;
				string label = $"{kind}, rotation {turn}";
				AssertInt(room.Doors.Count).OverrideFailureMessage(label).IsEqual(door > 0 ? 1 : 0);
				if (room.Doors.Count > 0)
				{
					var leaf = room.Doors.Values.Single();
					AssertBool(leaf.Secret.IsSecret).OverrideFailureMessage(label).IsEqual(secret > 0);
					AssertFloat(space.CastMotion(query)[0]).OverrideFailureMessage($"{label}: closed").IsLess(1f);
					if (leaf.Secret.IsSecret) AssertBool(leaf.Secret.Reveal()).OverrideFailureMessage(label).IsTrue();
					leaf.IsOpen = true;
					await PhysicsFrames(2);
				}
				AssertFloat(space.CastMotion(query)[0]).OverrideFailureMessage(label).IsGreaterEqual(.99f);
				room.QueueFree();
				await PhysicsFrames(1);
			}
		}
	}

	/// <summary>A room's secret chance owns the doorway decision; the wall and discovery state follow the door.</summary>
	[TestCase]
	public async Task TreasuryChoosesItsOwnSecretDoor()
	{
		using var type = (RoomType)TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres").Duplicate();
		type.SecretDoorChance = 0;
		var plain = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
		try
		{
			AssertInt(plain.Room.Doors.Count).IsEqual(1);
			AssertBool(plain.Room.Doors.Values.Single().Secret.IsSecret).IsFalse();
		}
		finally { plain.Room.Free(); }
		type.SecretDoorChance = 1;
		var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
		try
		{
			AssertInt(blueprint.Room.Doors.Count).IsEqual(1);
			var door = blueprint.Room.Doors.Values.Single();
			AssertBool(door.Secret.IsSecret).IsTrue();
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

	/// <summary>The treasury's chest is the room's reward: whatever the seed or depth, opening it yields gold and nothing else.</summary>
	[TestCase]
	public void TreasuryChestAlwaysHoldsGold()
	{
		var blueprint = RoomAssembler.Build(TestFactories.Load<RoomType>($"{RoomTypes}treasury.tres"), 23);
		try
		{
			var profile = blueprint.Room.FindChildren("*", "", true, false).OfType<Chest>().Single().LootTable.Profile;
			foreach (uint depth in new uint[] { 1, 3, 9 })
				for (ulong seed = 1; seed <= 3; seed++)
				{
					using var rng = GenerationRandom.Create(seed, GenerationRandom.Loot, "test.treasury");
					var rewards = LootTableComponent.GenerateOutcome(profile, depth, rng, "test.treasury");
					AssertInt(rewards.Count).IsEqual(1);
					AssertBool(rewards[0].Item is Gold && rewards[0].Quantity > 0).IsTrue();
				}
		}
		finally { blueprint.Room.Free(); }
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
			string before = string.Join(",", blueprint.Grid.Save());
			AssertBool(blueprint.TryPlace(group.Compose(blueprint, rng), at)).IsFalse();
			AssertInt(blueprint.Pieces.Count).IsEqual(0);
			AssertString(string.Join(",", blueprint.Grid.Save())).IsEqual(before);
			member.Required = false;
			AssertBool(blueprint.TryPlace(group.Compose(blueprint, rng), at)).IsTrue();
			AssertInt(blueprint.Pieces.Count).IsEqual(1);
		}
		finally { blueprint.Room.Free(); }
	}

	private static string Signature(RoomBlueprint blueprint) => string.Join("|", blueprint.Pieces.Select(piece =>
		$"{piece.Prop.Id}:{piece.Variant}:{piece.Transform}"));
}