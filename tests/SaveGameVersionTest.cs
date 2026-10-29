namespace RogueGauntlet.Tests;

using System.Text.Json;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Development saves must carry the exact supported version; no implicit conversion.</summary>
[TestSuite]
public class SaveGameVersionTest
{
	[TestCase("{}")]
	[TestCase("{\"Version\":41}")]
	[TestCase("{\"Version\":19}")]
	[TestCase("{\"Version\":18}")]
	[TestCase("{\"Version\":17}")]
	[TestCase("{\"Version\":16}")]
	[TestCase("{\"Version\":15}")]
	[TestCase("{\"Version\":14}")]
	[TestCase("{\"Version\":13}")]
	[TestCase("{\"Version\":4}")]
	[TestCase("{\"Version\":99999}")]
	[TestCase("{\"Version\":null}")]
	[TestCase("{\"Version\":\"5\"}")]
	[TestCase("[]")]
	public void IncompatibleOrMissingVersionIsRejected(string json)
	{
		bool rejected = false;
		try
		{
			SaveService.DeserializeCurrentVersion(json);
		}
		catch (JsonException)
		{
			rejected = true;
		}
		AssertBool(rejected).IsTrue();
	}

	[TestCase]
	public void CurrentSavePreservesSeedAndGameplayCounter()
	{
		var save = new SaveGame { Seed = 12345, DungeonDepth = 3, LootRollsConsumed = 17 };
		SaveGame restored = SaveService.DeserializeCurrentVersion(JsonSerializer.Serialize(save));
		AssertBool(restored.Seed == save.Seed).IsTrue();
		AssertInt((int)restored.DungeonDepth).IsEqual(3);
		AssertInt((int)restored.LootRollsConsumed).IsEqual(17);
	}
}