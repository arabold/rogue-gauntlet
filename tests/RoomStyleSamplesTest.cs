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
					int bays = first.Pieces.Count(piece => piece.Prop.Id == "sample.bay_junction");
					AssertBool(central == 3 && bays == 0 || central == 0 && bays is 1 or 2).IsTrue();
					AssertInt(first.Pieces.Count(piece => piece.Prop.Id == "sample.bay_end")).IsEqual(bays);
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

	/// <summary>One depot definition offers a central T or wall-attached storage bays, with complete end pieces.</summary>
	[TestCase("central_partition", 0)]
	[TestCase("bays_one_side", 1)]
	[TestCase("bays_both_sides", 2)]
	public void DepotLayoutsKeepTheirPartitionsCompleteAndTheirBaysOpen(string layoutId, int sides)
	{
		using var type = (RoomType)TestFactories.Load<RoomType>($"{Samples}partitioned_depot.tres").Duplicate();
		var layout = (ChoiceStep)type.Steps[0];
		type.Steps = [layout.Options.Single(option => option.Id == layoutId), .. type.Steps.Skip(1)];
		var ends = new System.Collections.Generic.HashSet<string>();
		foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8, 31, 60, 65, 95 })
		{
			var blueprint = RoomAssembler.Build(type, seed);
			try
			{
				AssertBool(blueprint.IsAcceptable).OverrideFailureMessage($"{layoutId} seed {seed}").IsTrue();
				AssertString(RoomAssemblyTest.FloorCoverage(blueprint)).IsEmpty();
				var junctions = blueprint.Pieces.Where(piece => piece.Prop.Id == "sample.bay_junction").ToArray();
				var caps = blueprint.Pieces.Where(piece => piece.Prop.Id == "sample.bay_end").ToArray();
				AssertInt(junctions.Length).IsEqual(sides);
				AssertInt(caps.Length).IsEqual(sides);
				AssertInt(junctions.Select(piece => Mathf.Sign(piece.Transform.Origin.X - blueprint.Interior.GetCenter().X)).Distinct().Count()).IsEqual(sides);
				AssertInt(blueprint.Pieces.Count(piece => piece.Prop.Id == "sample.partition_wall")).IsEqual(sides == 0 ? 3 : 0);
				AssertBool(blueprint.Room.GetNode("Props").GetChildren().OfType<Door>().Any(door => door.Interior != null)).IsFalse();
				foreach (var cap in caps) ends.Add(cap.Prop.Items[cap.Variant]);
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