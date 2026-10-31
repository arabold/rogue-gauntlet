namespace RogueGauntlet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Generated room identities reuse footprint/content policies while preserving furniture, passage and deterministic structure.</summary>
[TestSuite, RequireGodotRuntime]
public class ModularRoomTest
{
	private const string Root = "res://scenes/levels/garrison/rooms/";

	[TestCase("barracks", 0)]
	[TestCase("barracks", 1)]
	[TestCase("barracks", 2)]
	[TestCase("cellar", 0)]
	[TestCase("cellar", 1)]
	[TestCase("cellar", 2)]
	public void EveryLayoutWorksWithAndWithoutColumnsAndRotatesWithoutChangingRequiredContent(string kind, int index)
	{
		var recipe = Load(kind);
		var variant = recipe.Layouts[index];
		recipe.Layouts = [variant];
		AssertArray(recipe.Validate()).IsEmpty();
		for (ulong seed = 1; seed <= 3; seed++)
		{
			string floor = "", slots = "";
			for (int enabled = 0; enabled <= 1; enabled++)
			{
				recipe.ColumnChance = enabled;
				using var rng = new RandomNumberGenerator { Seed = seed };
				var room = new Room();
				try
				{
					AssertBool(recipe.PopulateRoom(room, rng, furnish: false)).IsTrue();
					AssertString(room.GetMeta("room_layout_variant").AsString()).IsEqual(variant.Id);
					AssertInt(room.GetChildren().OfType<RoomFurnishingSlot>().Count()).IsGreater(1);
					int area = RoomContentLayout.Describe(room).Tiles.Count;
					AssertBool(room.Map.Width <= 4 && room.Map.Height <= 5 && area <= 15).IsTrue();
					if (variant.BaseLayout.Shape == RoomShapeArchetype.LShape)
					{
						int cutWidth = room.Map.Width - variant.BaseLayout.LArmWidthTiles;
						int cutHeight = room.Map.Height - variant.BaseLayout.LArmWidthTiles;
						AssertBool(cutWidth >= 2 && cutHeight >= 2).IsTrue();
						AssertInt(area).IsEqual(room.Map.Width * room.Map.Height - cutWidth * cutHeight);
					}
					foreach (var cell in room.DecorationGridMap.GetUsedCells())
						AssertBool(room.DecorationGridMap.MeshLibrary.GetItemMesh(room.DecorationGridMap.GetCellItem(cell)).GetAabb().Size.Y >= 3.5f).IsTrue();
					if (enabled == 0)
					{
						AssertInt(room.DecorationGridMap.GetUsedCells().Count).IsEqual(0);
						floor = FloorSignature(room);
						slots = SlotSignature(room);
					}
					else
					{
						AssertString(FloorSignature(room)).IsEqual(floor);
						AssertString(SlotSignature(room)).IsEqual(slots);
					}
					for (int turn = 0; turn < 4; turn++)
					{
						try { AssertRequiredSlotsFit(room, recipe); }
						catch (InvalidOperationException error) { throw new InvalidOperationException($"{variant.Id} seed={seed} columns={enabled} turn={turn}: {error.Message}", error); }
						room.Rotate(1);
					}
					AssertString(FloorSignature(room)).IsEqual(floor);
					AssertString(SlotSignature(room)).IsEqual(slots);
				}
				finally { room.Free(); }
			}
		}
		// Columns are optional when the complete symmetric group cannot fit a compact footprint.
	}

	[TestCase]
	public void TallColumnsFitBesideBedsInACompactBarracksWithoutBlockingPassage()
	{
		var recipe = Load("barracks");
		recipe.Layouts = [recipe.Layouts[0]];
		recipe.Layouts[0].BaseLayout.MinimumSize = recipe.Layouts[0].BaseLayout.MaximumSize = new(3, 3);
		var bedsLayout = (WallBedsRoomLayout)recipe.Layouts[0].Contents;
		bedsLayout.MinimumBeds = bedsLayout.MaximumBeds = 2;
		// Isolate the column pattern from optional bedside choices.
		bedsLayout.Cluster.BoxChance = bedsLayout.Cluster.SideboardChance = bedsLayout.Cluster.DecorationChance = bedsLayout.Cluster.MirrorChance = 0;
		bedsLayout.Cluster.Bed.Variations = [];
		recipe.ColumnChance = 1;
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			AssertBool(recipe.PopulateRoom(room, rng, furnish: false)).IsTrue();
			AssertInt(room.Map.Width).IsEqual(3);
			AssertInt(room.Map.Height).IsEqual(3);
			var columns = RoomArrangement.GetGridBounds(room.DecorationGridMap, room.DecorationGridMap.Transform);
			AssertInt(columns.Count).IsGreater(0);
			var beds = RoomDressingProfile.GetBounds(room.GetChildren().OfType<RoomFurnishingSlot>()
				.Select(slot => slot.Resolve(0, room.LocalToTile(slot.Position), slot.Transform)));
			AssertBool(columns.Any(column => beds.Any(bed => HorizontalGap(column, bed) < .8f))).IsTrue();
			AssertBool(columns.Any(column => beds.Any(bed => WallArrangementMount.Overlaps(column, bed)))).IsFalse();
			AssertRequiredSlotsFit(room, recipe);
		}
		finally { room.Free(); }
	}

	[TestCase(0)]
	[TestCase(3)]
	public void LLayoutsRejectMissingArmsAndBarelyNotchedRectangles(int armWidth)
	{
		var recipe = Load("barracks");
		var layout = recipe.Layouts[2].BaseLayout;
		layout.MinimumSize = layout.MaximumSize = new(4, 4);
		layout.LArmWidthTiles = armWidth;
		AssertBool(layout.Validate().Length > 0).IsTrue();
	}

	private static float HorizontalGap(Aabb a, Aabb b)
	{
		float x = Mathf.Max(0, Mathf.Max(a.Position.X - b.End.X, b.Position.X - a.End.X));
		float z = Mathf.Max(0, Mathf.Max(a.Position.Z - b.End.Z, b.Position.Z - a.End.Z));
		return new Vector2(x, z).Length();
	}

	[TestCase(1, 1)]
	[TestCase(1, 4)]
	[TestCase(2, 4)]
	public void NarrowFootprintsPreserveEveryEntranceAndRotateWithAnExplicitEmptyLayout(int width, int length)
	{
		var recipe = Load("passage");
		recipe.MinimumFloorTiles = 1;
		recipe.Layouts[0].BaseLayout.MinimumSize = recipe.Layouts[0].BaseLayout.MaximumSize = new(width, length);
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			AssertArray(recipe.Validate()).IsEmpty();
			AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
			AssertInt(room.Map.Width).IsEqual(width);
			AssertInt(room.Map.Height).IsEqual(length);
			AssertInt(RoomContentLayout.Describe(room).Tiles.Count).IsEqual(width * length);
			AssertInt(room.GetChildren().OfType<RoomFurnishingSlot>().Count()).IsEqual(0);
			for (int turn = 0; turn < 4; turn++)
			{
				AssertInt(RoomContentLayout.Describe(room).ConnectorTiles.Sum(tile => room.Map.GetConnectorDirections(tile.X, tile.Y).Count)).IsEqual(2);
				AssertRequiredSlotsFit(room, recipe);
				room.Rotate(1);
			}
		}
		finally { room.Free(); }
	}

	[TestCase]
	public void MeasuredWallRunsRepeatIndependentPiecesAndLongerWallsFitMoreBeds()
	{
		var recipe = Load("barracks");
		var variant = recipe.Layouts[0];
		recipe.Layouts = [variant];
		var layout = (WallBedsRoomLayout)variant.Contents;
		layout.MaximumBeds = 16;
		layout.AllowedWalls = (int)RoomMarkerDirection.West;
		layout.Cluster.WallDecorations = [layout.Cluster.WallDecorations.First(definition => definition.Id.Contains("wall_torch"))];
		layout.Cluster.DecorationChance = 1;
		layout.Cluster.BoxChance = layout.Cluster.SideboardChance = 1;
		layout.Cluster.MirrorChance = 0;
		layout.Cluster.Bed.Variations = layout.Cluster.Box.Variations = layout.Cluster.Sideboard.Variations = [];
		recipe.ColumnChance = 0;
		int previous = 0;
		foreach (int length in new[] { 3, 5 })
		{
			variant.BaseLayout.MinimumSize = variant.BaseLayout.MaximumSize = new(3, length);
			using var rng = new RandomNumberGenerator { Seed = 23 };
			var room = new Room();
			try
			{
				AssertBool(recipe.PopulateRoom(room, rng, furnish: false)).IsTrue();
				var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
				int beds = slots.Count(slot => slot.Definition.Id == layout.Cluster.Bed.Id);
				AssertInt(beds).IsGreater(previous);
				int boxes = slots.Count(slot => slot.Definition.Id == layout.Cluster.Box.Id);
				int sideboards = slots.Count(slot => slot.Definition.Id == layout.Cluster.Sideboard.Id);
				AssertBool(boxes > 0 && boxes <= beds).IsTrue();
				AssertBool(sideboards > 0 && sideboards <= beds).IsTrue();
				AssertBool(slots.Any(slot => slot.Definition.Placement == ArrangementPlacement.Wall)).IsTrue();
				AssertInt(slots.Select(slot => slot.Id).Distinct().Count()).IsEqual(slots.Length);
				AssertRequiredSlotsFit(room, recipe);
				previous = beds;
			}
			finally { room.Free(); }
		}
	}

	[TestCase]
	public void AlchemistComposesTheSameSleepingClusterWithLibraryShelves()
	{
		var recipe = Load("alchemist");
		var steps = (CompositeRoomLayout)recipe.Layouts[0].Contents;
		var beds = (WallBedsRoomLayout)steps.Steps[0];
		var shelves = steps.Steps.OfType<ShelfRowsRoomLayout>().Single();
		for (ulong seed = 1; seed <= 6; seed++)
		{
			using var rng = new RandomNumberGenerator { Seed = seed };
			var room = new Room();
			try
			{
				AssertBool(recipe.PopulateRoom(room, rng, furnish: false)).IsTrue();
				var slots = room.GetChildren().OfType<RoomFurnishingSlot>().ToArray();
				AssertInt(slots.Count(slot => beds.Cluster.Bed.Variants.Any(definition => definition.Id == slot.Definition.Id))).IsEqual(1);
				AssertBool(slots.Any(slot => shelves.Definitions.Any(definition => definition.Id == slot.Definition.Id))).IsTrue();
				AssertRequiredSlotsFit(room, recipe);
			}
			finally { room.Free(); }
		}
	}

	[TestCase]
	public void CompositeLayoutsRejectIndirectCyclesBeforeEnumeratingDefinitions()
	{
		var first = new CompositeRoomLayout();
		var second = new CompositeRoomLayout { Steps = [first] };
		first.Steps = [second];
		AssertBool(first.Validate().Length > 0).IsTrue();
		AssertArray(first.Definitions).IsEmpty();
		first.Steps = [];
		first.Dispose();
		second.Dispose();
	}

	[TestCase("library")]
	[TestCase("dining")]
	public void RepeatedRowsUseEveryAvailableVariantBeforeRepeatingAndReproduceChoices(string kind)
	{
		var recipe = TestFactories.Load<RoomGenerator>($"{Root}{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		RoomArrangementDefinition[] families;
		if (recipe is LibraryRoomGenerator library)
		{
			library.StackRows = [5];
			families = [library.WallBookcase, library.StackArrangement];
		}
		else
		{
			var dining = (DiningRoomGenerator)recipe;
			dining.TableRows = [3];
			families = [dining.TableArrangement];
		}
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var first = recipe.BuildRoom(rng);
		rng.Seed = 23;
		var second = recipe.BuildRoom(rng);
		try
		{
			AssertString(SlotSignature(first)).IsEqual(SlotSignature(second));
			foreach (var family in families)
			{
				var ids = family.Variants.Select(variant => variant.Id).ToArray();
				var selected = first.GetChildren().OfType<RoomFurnishingSlot>().Select(slot => slot.Definition.Id).Where(ids.Contains).ToArray();
				AssertInt(selected.Length).IsGreater(ids.Length);
				AssertInt(selected.Take(ids.Length).Distinct().Count()).IsEqual(ids.Length);
				var counts = ids.Select(id => selected.Count(used => used == id)).ToArray();
				AssertBool(counts.Max() - counts.Min() <= 1).IsTrue();
			}
		}
		finally { first.Free(); second.Free(); }
	}

	[TestCase]
	public void IncompleteAndRecursiveVariationPalettesReportConfigurationErrors()
	{
		var recipe = Load("barracks");
		var definition = ((WallBedsRoomLayout)recipe.Layouts[0].Contents).Cluster.Bed;
		definition.Variations = [null];
		AssertBool(recipe.Validate().Length > 0).IsTrue();
		definition.Variations = [definition];
		AssertBool(recipe.Validate().Length > 0).IsTrue();
		definition.Variations = [];
	}

	[TestCase]
	public void SharedPoliciesRemainImmutableAndScenePackingPreservesSlotIdentity()
	{
		var recipe = Load("barracks");
		var before = recipe.Layouts.SelectMany(layout => layout.Contents.Definitions).Select(entry => entry.MaximumPerRoom).ToArray();
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var first = recipe.BuildRoom(rng);
		rng.Seed = 23;
		var second = recipe.BuildRoom(rng);
		try
		{
			AssertString(FloorSignature(first)).IsEqual(FloorSignature(second));
			AssertString(SlotSignature(first)).IsEqual(SlotSignature(second));
			first.Rotate(1);
			AssertString(SlotSignature(first)).IsNotEqual(SlotSignature(second));
			AssertArray(recipe.Layouts.SelectMany(layout => layout.Contents.Definitions).Select(entry => entry.MaximumPerRoom).ToArray()).IsEqual(before);
			ProceduralRoomBuilder.SetOwnersForPacking(second, second);
			using var packed = new PackedScene();
			AssertBool(packed.Pack(second) == Error.Ok).IsTrue();
			var restored = packed.Instantiate<Room>();
			try { AssertString(SlotSignature(restored)).IsEqual(SlotSignature(second)); }
			finally { restored.Free(); }
		}
		finally { first.Free(); second.Free(); }
	}

	[TestCase]
	public void FailedCompositionLeavesExistingRoomUntouched()
	{
		var recipe = Load("cellar");
		recipe.Layouts = [recipe.Layouts[0]];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		var room = new Room();
		try
		{
			AssertBool(recipe.PopulateRoom(room, rng)).IsTrue();
			string before = FloorSignature(room) + SlotSignature(room);
			var contents = (PerimeterRoomLayout)recipe.Layouts[0].Contents;
			contents.MinimumCount = contents.Count = 32;
			recipe.CompositionAttempts = 2;
			bool failed = false;
			try { recipe.PopulateRoom(room, rng); }
			catch (InvalidOperationException) { failed = true; }
			AssertBool(failed).IsTrue();
			AssertString(FloorSignature(room) + SlotSignature(room)).IsEqual(before);
		}
		finally { room.Free(); }
	}

	[TestCase]
	public void ExactSlotsCanShareATileWithoutLosingTheirIndependentTransforms()
	{
		var content = new Node3D { Name = "Content" };
		var arrangement = new RoomArrangement { Content = content, PlacementBounds = new(-.25f, 0, -.25f, .5f, .5f, .5f) };
		arrangement.AddChild(content);
		content.Owner = arrangement;
		var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * .5f }, Position = Vector3.Up * .25f };
		content.AddChild(mesh);
		mesh.Owner = arrangement;
		using var scene = new PackedScene();
		AssertBool(scene.Pack(arrangement) == Error.Ok).IsTrue();
		arrangement.Free();
		var definition = new RoomArrangementDefinition { Id = "test.small", Scene = scene, MaximumPerRoom = 2 };
		var map = new MapData(5, 5);
		var region = new RoomRegion(0) { IsProcedural = true };
		for (int x = 0; x < 5; x++)
		for (int z = 0; z < 5; z++) { map.SetTile(x, z, MapTile.Room); region.Tiles.Add(new(x, z)); }
		var anchor = new Vector2I(2, 2);
		foreach (int side in new[] { -1, 1 })
			region.FurnishingSlots.Add(new(definition, 0, anchor, 0, [anchor],
				new(Basis.Identity, new Vector3(side * .8f, 0, 0)), SlotId: $"small:{side}"));
		var pool = new RoomArrangementPool
		{
			Entries = [definition], PlacementStrategy = new SlotArrangementStrategy(), MaximumPerRoom = 2,
			MaximumWallPerRoom = 0, MaximumOccupiedFraction = 1,
		};
		var plan = pool.PlacementStrategy.Plan(map, [region], pool, 23, 0, tile => map.TileToWorld(tile), [], []);
		AssertInt(plan.Count).IsEqual(2);
		AssertInt(plan.Select(placement => placement.SlotId).Distinct().Count()).IsEqual(2);
		AssertBool(plan[0].Transform != plan[1].Transform).IsTrue();
	}

	[TestCase]
	public void GeneratedFailureDoesNotFallBackToAnAuthoredStandardRoom()
	{
		var factory = TestFactories.Load<MixedRoomFactory>("res://scenes/levels/garrison/factories/garrison_mixed_room_factory.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		factory.RoomGenerators = [];
		factory.RoomBuilder = new ProceduralRoomBuilder();
		((DungeonRoomFactory)factory.AuthoredFactory).StandardRoomScenePaths = ["res://scenes/levels/dungeon/rooms/hall.tscn"];
		using var rng = new RandomNumberGenerator { Seed = 23 };
		bool failed = false;
		try { factory.CreateStandardRoom(rng); }
		catch (InvalidOperationException error) { failed = error.Message.Contains("authored substitution"); }
		AssertBool(failed).IsTrue();
	}

	private static ModularRoomGenerator Load(string kind) => TestFactories.Load<ModularRoomGenerator>(
		$"{Root}{kind}/garrison_{kind}_generator.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);

	private static string FloorSignature(Room room) => string.Join("|", room.FloorGridMap.GetUsedCells().OrderBy(cell => cell.X).ThenBy(cell => cell.Z)
		.Select(cell => $"{cell}:{room.FloorGridMap.GetCellItem(cell)}:{room.FloorGridMap.GetCellItemOrientation(cell)}"));
	private static string SlotSignature(Room room) => string.Join("|", room.GetChildren().OfType<RoomFurnishingSlot>().Select(slot => $"{slot.Id}:{slot.Definition.Id}:{slot.Position.Snapped(Vector3.One * .0001f)}:{Mathf.RoundToInt(slot.Rotation.Y / (Mathf.Pi / 2))}"));

	internal static void AssertRequiredSlotsFit(Room room, RoomGenerator recipe)
	{
		var region = RoomContentLayout.Describe(room);
		foreach (var slot in room.GetChildren().OfType<RoomFurnishingSlot>())
			region.FurnishingSlots.Add(slot.Resolve(region.Id, room.LocalToTile(slot.Position), slot.Transform));
		var pool = recipe.CreateInteriorPool(room);
		var physical = RoomArrangement.GetGridBounds(room.DecorationGridMap, room.DecorationGridMap.Transform)
			.Concat(room.InteriorStructure == null ? [] : RoomArrangement.GetStaticPropBounds(room.InteriorStructure, room.InteriorStructure.Transform)).ToArray();
		var occupied = physical.Concat(InteriorDoorComponent.CollectClearances(room, Transform3D.Identity)).ToArray();
		var walls = RoomArrangement.GetGridBounds(room.WallGridMap, room.WallGridMap.Transform);
		var plan = pool.PlacementStrategy.Plan(room.Map, [region], pool, 23, 0, tile => room.TileCenterCell(tile.X, tile.Y), occupied, walls, physical);
		AssertInt(plan.Count).IsEqual(region.FurnishingSlots.Count);
		AssertBool(new RoomPassageValidator(room.Map, region.Tiles.ToHashSet(), region.ConnectorTiles,
			tile => room.TileCenterCell(tile.X, tile.Y), physical.Concat(walls).ToArray(), pool.PassageRadius)
			.IsConnected(RoomDressingProfile.GetBounds(plan, true))).IsTrue();
	}
}