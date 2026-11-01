namespace RogueGauntlet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Isolated room studies use production assembly, keep their defining content and author real gold rewards.</summary>
[TestSuite, RequireGodotRuntime]
public class RoomStyleSamplesTest
{
	private const string Samples = "res://scenes/debug/rooms/samples/";

	[TestCase("partitioned_depot")]
	[TestCase("compact_lockup")]
	[TestCase("treasury")]
	public void SamplesReproduceConnectedLayoutsAndTheirDefiningContent(string name)
	{
		var type = TestFactories.Load<RoomType>($"{Samples}{name}.tres");
		AssertArray(type.Validate()).IsEmpty();
		foreach (ulong seed in new ulong[] { 1, 23, 41, 99, 7919, 104729 })
		{
			var first = RoomAssembler.Build(type, seed);
			var second = RoomAssembler.Build(type, seed);
			try
			{
				AssertBool(first.IsAcceptable).OverrideFailureMessage($"{name} seed {seed}").IsTrue();
				AssertString(Signature(second)).IsEqual(Signature(first));
				int maximumTiles = name switch { "treasury" => 9, "compact_lockup" => 16, _ => 20 };
				AssertInt(first.Footprint.Count).IsLessEqual(maximumTiles);
				AssertString(RoomAssemblyTest.FloorCoverage(first)).IsEmpty();
				AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "sample.lit_sconce")).IsGreaterEqual(2);
				var nodes = first.Room.FindChildren("*", "", true, false);
				if (name == "partitioned_depot")
					AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "sample.partition_wall")).IsEqual(3);
				if (name == "compact_lockup")
					AssertInt(nodes.OfType<Door>().Count(door => door.Interior != null)).IsEqual(2);
				if (name == "treasury")
				{
					var chest = nodes.OfType<Chest>().Single();
					AssertString(chest.LootTable.Profile.ResourcePath).IsEqual($"{Samples}treasury_gold.tres");
					AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "sample.yellow_banner")).IsEqual(1);
					AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "bookcase")).IsEqual(1);
					AssertInt(first.Pieces.Count(piece => piece.Prop.Id is "trunk" or "small_box")).IsGreaterEqual(2);
					AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "sample.decorative_coins")).IsGreaterEqual(3);
				}
			}
			finally
			{
				first.Room.Free();
				second.Room.Free();
			}
		}
	}

	[TestCase]
	public void DirtStaysInsideCellRowsWithoutChangingFurnitureOrWalkability()
	{
		var type = TestFactories.Load<RoomType>($"{Samples}compact_lockup.tres");
		using var plainType = (RoomType)type.Duplicate();
		using var cells = (CellsStep)type.Steps.OfType<CellsStep>().Single().Duplicate();
		cells.CellFloor = null;
		plainType.Steps = type.Steps.Select(step => step is CellsStep ? cells : step).ToArray();
		var dirt = RoomAssembler.Build(type, 23);
		var plain = RoomAssembler.Build(plainType, 23);
		try
		{
			AssertString(Signature(dirt)).IsEqual(Signature(plain));
			AssertString(Convert.ToBase64String(dirt.Grid.Save())).IsEqual(Convert.ToBase64String(plain.Grid.Save()));
			AssertInt(dirt.FloorPatches.Count).IsEqual(1);
			AssertInt(plain.FloorPatches.Count).IsEqual(0);
			var patch = dirt.FloorPatches.Single();
			var dirtItems = patch.Material.Large.Keys.Concat(patch.Material.Small.Keys).Concat(patch.Material.Wear.Keys).ToHashSet();
			var grid = dirt.Room.FloorGridMap;
			int inside = 0, outside = 0;
			foreach (Vector3I cell in grid.GetUsedCells())
			{
				if (dirtItems.Contains(grid.MeshLibrary.GetItemName(grid.GetCellItem(cell))))
				{
					AssertBool(patch.Area.HasPoint(new Vector2(cell.X, cell.Z))).IsTrue();
					inside++;
				}
				else outside++;
			}
			AssertInt(inside).IsGreater(0);
			AssertInt(outside).IsGreater(0);
			AssertString(RoomAssemblyTest.FloorCoverage(dirt)).IsEmpty();
		}
		finally { dirt.Room.Free(); plain.Room.Free(); }
	}

	[TestCase]
	public void RequiredGroupMemberRollsBackTheAnchorWhenItCannotFit()
	{
		var type = TestFactories.Load<RoomType>($"{Samples}partitioned_depot.tres");
		var blueprint = RoomAssembler.Build(type, 23, new RoomAssemblyOptions { Furnish = false });
		try
		{
			var wall = TestFactories.Load<Prop>($"{Samples}props/partition_wall.tres");
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
	public void TreasuryChestGuaranteesAuthoredGoldWithoutChangingTheOrdinaryChestProfile()
	{
		var profile = TestFactories.Load<LootDropProfile>($"{Samples}treasury_gold.tres");
		AssertArray(profile.Table.Validate()).IsEmpty();
		foreach (uint depth in new uint[] { 1, 3, 9 })
		{
			using var rng = GenerationRandom.Create(23, GenerationRandom.Loot, "test.treasury");
			var rewards = LootTableComponent.GenerateOutcome(profile, depth, rng, "test.treasury");
			AssertInt(rewards.Count).IsEqual(1);
			AssertBool(rewards[0].Item is Gold).IsTrue();
			AssertInt(rewards[0].Quantity).IsEqual(240);
		}
		var ordinary = TestFactories.Load<LootDropProfile>("res://scenes/items/loot/drops/chest_rare.tres");
		AssertObject(ordinary).IsNotSame(profile);
	}

	[TestCase]
	public async Task SampleWorkbenchPreviewsEachRecipeWithoutAddingItToProductionFactories()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/rooms/room_style_samples.tscn");
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		AssertInt(harness.Recipes.Count).IsEqual(3);
		for (int index = 0; index < harness.Recipes.Count; index++)
		{
			harness.SelectRecipe(index);
			harness.Regenerate();
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).IsEmpty();
			AssertBool(harness.PreviewBlueprint.IsAcceptable).IsTrue();
			AssertBool(((AssembledRoomFactory)harness.Generator.Rooms).RoomTypes.Contains(harness.Recipes[index])).IsFalse();
			harness.RotatePreview(1);
			harness.Regenerate();
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).IsEmpty();
		}
		// A floor's realm must not filter the isolated recipes when returning to room view.
		harness.Mode = RoomPreviewMode.Floor;
		harness.SelectRealm(1);
		harness.Regenerate();
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		harness.Mode = RoomPreviewMode.Room;
		harness.SelectRecipe(1);
		harness.Regenerate();
		await runner.SimulateFrames(1);
		AssertString(harness.LastError).IsEmpty();
		AssertString(harness.PreviewRoom.Type.Id).IsEqual("sample.compact_lockup");
		AssertBool(harness.PreviewBlueprint.IsAcceptable).IsTrue();
	}

	private static string Signature(RoomBlueprint blueprint) => string.Join("|", blueprint.Pieces.Select(piece =>
		$"{piece.Prop.Id}:{piece.Variant}:{piece.Transform}"));
}