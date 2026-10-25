namespace RogueGauntlet.Tests;

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Verifies actual streams, selected encounter scenes/transforms, floor loot and reward rolls.
/// Comparisons are within a build; they do not freeze historical generator outputs.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GenerationBaselineTest
{
	[TestCase]
	public void DrawingOneDomainDoesNotAdvanceOtherDomainsOrGlobalRandom()
	{
		var expected = new LevelGenerationContext(42);
		var actual = new LevelGenerationContext(42);
		GD.Seed(1234);
		uint globalExpected = GD.Randi();
		GD.Seed(1234);
		for (int i = 0; i < 100; i++)
		{
			actual.Items.Randi();
			actual.Decoration.Randi();
		}
		AssertBool(actual.Layout.Randi() == expected.Layout.Randi()).IsTrue();
		AssertBool(actual.Encounters.Randi() == expected.Encounters.Randi()).IsTrue();
		AssertBool(GD.Randi() == globalExpected).IsTrue();
	}

	[TestCase]
	public void SourceRewardRollsAreIndependentOfActivationOrder()
	{
		var table = GD.Load<LootTable>("res://scenes/items/loot/loot_chest_common.tres");
		string a = RollReward(table, "chest:a");
		string b = RollReward(table, "monster:b");
		GD.Seed(9876);
		for (int i = 0; i < 100; i++) { GD.Randi(); }
		AssertString(RollReward(table, "monster:b")).IsEqual(b);
		AssertString(RollReward(table, "chest:a")).IsEqual(a);
	}

	[TestCase]
	public async Task RegenerationReproducesLayoutEncountersAndItemsWithoutAdvancingGameplayLoot()
	{
		var (runner, generator) = await TestFactories.LoadGenerator(new PackedRoomLayout(), TestFactories.AllProcedural());
		TestFactories.WireLootScenes(generator);
		generator.ItemCreationPolicy = GD.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");
		generator.MapWidth = generator.MapDepth = 40;
		generator.MaxRooms = 12;
		generator.MinChests = generator.MaxChests = 2;
		generator.MinLooseItems = generator.MaxLooseItems = 3;
		generator.DungeonDepth = 4;
		generator.Seed = GameSession.GetLevelSeed(12345, 4);
		uint lootCounter = GameSession.Instance?.LootRollsConsumed ?? 0;
		generator.GenerateMap();
		string first = Fingerprint(generator);
		AssertInt(generator.EnemySpawnPoints.Count).IsGreater(0);
		AssertInt(generator.NavigationRegion.GetChildren().OfType<LootableItem>().Count()).IsGreater(0);
		await runner.SimulateFrames(1);

		for (int i = 0; i < 100; i++) { GD.Randi(); }
		// Creation requests for other sources and unrelated streams must not affect regeneration.
		using var otherReward = generator.CreateSourceLootRng("another-source");
		otherReward.Randi();
		generator.GenerateMap();
		AssertString(Fingerprint(generator)).IsEqual(first);
		AssertBool((GameSession.Instance?.LootRollsConsumed ?? 0) == lootCounter).IsTrue();
		await runner.SimulateFrames(1);
	}

	private static string RollReward(LootTable table, string source)
	{
		using var rng = GenerationRandom.Create(42, GenerationRandom.Loot, source);
		var result = new StringBuilder();
		for (int i = 0; i < 5; i++)
		{
			LootTableItem entry = table.PickEntry(4, rng);
			result.Append(ItemFingerprint(ItemFactory.Create(entry.Item, GD.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres"), 4, rng, $"{source}:{i}"))).Append('|');
		}
		return result.ToString();
	}

	private static string Fingerprint(MapGenerator generator)
	{
		var result = new StringBuilder();
		for (int z = 0; z < generator.Map.Height; z++)
		{
			for (int x = 0; x < generator.Map.Width; x++)
			{
				result.Append((int)generator.Map.Tiles[x, z]);
			}
		}
		foreach (SpawnPoint spawn in generator.EnemySpawnPoints)
		{
			result.Append(spawn.PersistentId).Append(spawn.Scenes[0].ResourcePath)
				.Append(spawn.GlobalTransform);
		}
		foreach (Node3D node in generator.NavigationRegion.GetChildren().OfType<Node3D>())
		{
			if (node is not (Chest or LootableItem)) { continue; }
			result.Append(node.GlobalTransform).Append(node.GetMeta("persistent_id"));
			if (node is LootableItem pickup)
			{
				result.Append(pickup.Quantity).Append(ItemFingerprint(pickup.Item));
			}
		}
		return result.ToString();
	}

	private static string ItemFingerprint(Item item)
	{
		var result = new StringBuilder(item.DefinitionPath).Append(item.InstanceId);
		if (item is FoodItem food) { result.Append(food.IsPoisoned); }
		if (item is not EquipableItem equipment) { return result.ToString(); }
		result.Append(equipment.Quality).Append(equipment.QualityAdjective).Append(equipment.MagnitudeScale);
		foreach (RolledAffix affix in equipment.Affixes)
		{
			result.Append(affix.NameFragment);
			foreach (StatModifier modifier in affix.Modifiers)
			{
				result.Append(modifier.Stat).Append(modifier.Op).Append(modifier.Value);
			}
		}
		return result.ToString();
	}
}