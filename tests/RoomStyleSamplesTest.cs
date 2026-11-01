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
				int maximumTiles = name == "treasury" ? 9 : 20;
				AssertInt(first.Footprint.Count).IsLessEqual(maximumTiles);
				AssertString(RoomAssemblyTest.FloorCoverage(first)).IsEmpty();
				AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "sample.lit_sconce")).IsGreaterEqual(2);
				var nodes = first.Room.FindChildren("*", "", true, false);
				if (name == "partitioned_depot")
				{
					int central = first.Pieces.Count(piece => piece.Prop.Id == "sample.partition_wall");
					int junctions = first.Room.WallGridMap.GetUsedCells().Cast<Vector3I>().Count(cell =>
						first.Room.WallGridMap.MeshLibrary.GetItemName(first.Room.WallGridMap.GetCellItem(cell)) == "wall_Tsplit");
					AssertBool(central == 3 && junctions == 0 || central == 0 && junctions is 1 or 2).IsTrue();
				}
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

	/// <summary>Junctions replace perimeter pieces and their outward-facing ends join without overlapping other walls.</summary>
	[TestCase(0, 0)]
	[TestCase(1, 1)]
	[TestCase(2, 2)]
	public void DepotWallVariantsJoinWithoutOverlapping(int style, int sides)
	{
		var type = TestFactories.Load<RoomType>($"{Samples}partitioned_depot.tres");
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
				AssertInt(junctions.Length).IsEqual(sides);
				AssertInt(blueprint.Pieces.Count(piece => piece.Prop.Id == "sample.partition_wall")).IsEqual(sides == 0 ? 3 : 0);
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
					foreach (var part in new[] { junction, end })
					{
						var box = WallFootprints.FootprintWorld(grid.MeshLibrary.GetItemMesh(grid.GetCellItem(part)).GetAabb(), grid.GetCellItemOrientation(part), part);
						foreach (var other in cells.Where(cell => cell != part))
						{
							var otherBox = WallFootprints.FootprintWorld(grid.MeshLibrary.GetItemMesh(grid.GetCellItem(other)).GetAabb(), grid.GetCellItemOrientation(other), other);
							float overlapX = Mathf.Min(box.End.X, otherBox.End.X) - Mathf.Max(box.Position.X, otherBox.Position.X);
							float overlapZ = Mathf.Min(box.End.Z, otherBox.End.Z) - Mathf.Max(box.Position.Z, otherBox.Position.Z);
							AssertBool(overlapX > .05f && overlapZ > .05f).OverrideFailureMessage($"{part} overlaps {other}").IsFalse();
						}
					}
				}
			}
			finally { blueprint.Room.Free(); }
		}
		if (sides > 0) AssertInt(ends.Count).IsEqual(2);
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
	public async Task SampleWorkbenchReusesTheProductionPrisonAndKeepsNewStudiesOutsideThePool()
	{
		using var runner = TestFactories.LoadScene("res://scenes/debug/rooms/room_style_samples.tscn");
		await runner.SimulateFrames(2);
		var harness = (RoomGenerationHarness)runner.Scene();
		AssertInt(harness.Recipes.Count).IsEqual(3);
		var pool = (AssembledRoomFactory)harness.Generator.Rooms;
		AssertObject(harness.Recipes[1]).IsSame(pool.RoomTypes.Single(type => type.Id == "prison"));
		for (int index = 0; index < harness.Recipes.Count; index++)
		{
			harness.SelectRecipe(index);
			harness.Regenerate();
			await runner.SimulateFrames(1);
			AssertString(harness.LastError).IsEmpty();
			AssertBool(harness.PreviewBlueprint.IsAcceptable).IsTrue();
			AssertBool(pool.RoomTypes.Contains(harness.Recipes[index])).IsEqual(index == 1);
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
		AssertString(harness.PreviewRoom.Type.Id).IsEqual("prison");
		AssertBool(harness.PreviewBlueprint.IsAcceptable).IsTrue();
	}

	private static string Signature(RoomBlueprint blueprint) => string.Join("|", blueprint.Pieces.Select(piece =>
		$"{piece.Prop.Id}:{piece.Variant}:{piece.Transform}"));
}