namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Furniture-led room identities survive resizing, rotation and the common passage checks.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomConceptTest
{
	private const string Root = "res://scenes/levels/garrison/";

	[TestCase("dining", 1, 3)]
	[TestCase("dining", 3, 3)]
	[TestCase("library", 5, 5)]
	[TestCase("library", 7, 5)]
	[TestCase("library", 7, 7)]
	public void RequiredCompositionScalesAndRotatesWithoutLosingItsEntrances(string kind, int size, int width)
	{
		var recipe = TestFactories.Load<RoomGenerator>($"{Root}rooms/{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		if (recipe is DiningRoomGenerator dining) dining.TableRows = [size];
		if (recipe is LibraryRoomGenerator library) { library.StackRows = [size - 2]; library.ColumnsPerBank = (width - 3) / 2; }
		AssertArray(recipe.Validate()).IsEmpty();
		int expected = kind switch
		{
			"dining" => size,
			_ => (width, size) switch { (5, 5) => 24, (5, 7) => 32, _ => 46 },
		};
		for (ulong seed = 1; seed <= 2; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = new Room();
			try
			{
				AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
				AssertInt(room.GetChildren().OfType<RoomArrangement>().Count(a => !a.GetMeta("is_room_detail", false).AsBool())).IsEqual(expected);
				foreach (var furnishing in room.GetChildren().OfType<RoomArrangement>())
				{
					room.RemoveChild(furnishing);
					furnishing.Free();
				}
				for (int turn = 0; turn < 4; turn++)
				{
					var region = new RoomRegion(11) { IsProcedural = true, Purpose = room.Purpose };
					for (int x = 0; x < room.Map.Width; x++)
					for (int z = 0; z < room.Map.Height; z++)
						if (room.Map.IsWalkable(x, z))
						{
							region.Tiles.Add(new(x, z));
							if (room.Map.GetConnectorDirections(x, z).Count > 0) region.ConnectorTiles.Add(new(x, z));
						}
					foreach (var slot in room.GetChildren().OfType<RoomFurnishingSlot>())
						region.FurnishingSlots.Add(slot.Resolve(region.Id, room.LocalToTile(slot.Position), slot.Transform));
					Vector3 Center(Vector2I tile) => (Vector3)room.TileCenterCell(tile.X, tile.Y);
					var pool = recipe.CreateInteriorPool();
					var walls = RoomArrangement.GetGridBounds(room.WallGridMap, room.WallGridMap.Transform);
					var plan = pool.PlacementStrategy.Plan(room.Map, [region], pool, seed, 0, Center, [], walls);
					AssertInt(plan.Count).IsEqual(expected);
					AssertInt(region.ConnectorTiles.Count).IsEqual(2);
					var blocking = new List<Aabb>();
					foreach (var placement in plan)
					{
						AssertBool(placement.Occupied.Any(region.ConnectorTiles.Contains)).IsFalse();
						if (placement.Definition.WallPlaneDistance > 0)
						{
							var outward = ConnectedArrangementStrategy.Rotate(Vector2I.Up, placement.QuarterTurns);
							var normal = new Vector3(outward.X, 0, outward.Y);
							AssertBool(Mathf.Abs((placement.Transform.Origin - Center(placement.Anchor)).Dot(normal) - 1.195f) < .002f).IsTrue();
						}
						AssertBool(region.FurnishingSlots.Any(slot => slot.Definition.Id == placement.Definition.Id && slot.Anchor == placement.Anchor && (placement.Definition.AlignToWall || slot.Transform.IsEqualApprox(placement.Transform)))).IsTrue();
						var instance = placement.Definition.Instantiate();
						try { blocking.AddRange(instance.GetBlockingBounds().Select(box => placement.Transform * box)); }
						finally { instance.Free(); }
					}
					AssertBool(new RoomPassageValidator(room.Map, region.Tiles.ToHashSet(), region.ConnectorTiles,
						Center, walls, pool.PassageRadius).IsConnected(blocking)).IsTrue();
					string Signature(IEnumerable<RoomArrangementPlacement> placements) => string.Join("|", placements.Select(p => $"{p.Definition.Id}:{p.Transform}"));
					AssertString(Signature(plan)).IsEqual(Signature(pool.PlacementStrategy.Plan(room.Map, [region], pool, seed, 0, Center, [], walls)));
					room.Rotate(1);
				}
			}
			finally { room.Free(); }
		}
		AssertBool(recipe.CreateInteriorPool().Entries.All(definition => definition.MaximumPerRoom == 64)).IsTrue();
	}

	[TestCase]
	public void PackingAndGenericRerollsPreserveAuthoredFurnitureAndRemoveObsoleteSlots()
	{
		var recipe = TestFactories.Load<DiningRoomGenerator>(Root + "rooms/dining/garrison_dining_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		recipe.TableRows = [3];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
			ProceduralRoomBuilder.SetOwnersForPacking(room, room);
			using var packed = new PackedScene();
			AssertBool(packed.Pack(room) == Error.Ok).IsTrue();
			var restored = packed.Instantiate<Room>();
			AssertInt(restored.GetChildren().OfType<RoomFurnishingSlot>().Count()).IsEqual(3);
			AssertInt(restored.GetChildren().OfType<RoomArrangement>().Count(a => !a.GetMeta("is_room_detail", false).AsBool())).IsEqual(3);
			restored.Free();
			AssertBool(recipe.ShellBuilder.PopulateRoom(room, rng)).IsTrue();
			AssertInt(room.GetChildren().OfType<RoomFurnishingSlot>().Count()).IsEqual(0);
			AssertObject(room.Generator).IsNull();
			AssertInt(recipe.TableArrangement.MaximumPerRoom).IsEqual(1);
		}
		finally { room.Free(); }
	}
}