namespace RogueGauntlet.Tests;

using System.Text.Json;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Development saves must carry the exact supported version; no implicit conversion.</summary>
[TestSuite]
public class SaveGameVersionTest
{
	[TestCase]
	public void IncompatibleOrMissingVersionIsRejected()
	{
		// Missing, older, newer, null, mistyped, and not an object at all.
		string[] saves = ["{}", $"{{\"Version\":{SaveGame.CurrentVersion - 1}}}", $"{{\"Version\":{SaveGame.CurrentVersion + 1}}}",
			"{\"Version\":null}", $"{{\"Version\":\"{SaveGame.CurrentVersion}\"}}", "[]"];
		foreach (string json in saves)
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
			AssertBool(rejected).OverrideFailureMessage($"accepted {json}").IsTrue();
		}
	}

	[TestCase]
	public void CurrentSavePreservesRunIdentityAndGameplayCounter()
	{
		var save = new SaveGame { Seed = 12345, DungeonDepth = 3, LootRollsConsumed = 17, CharacterClassId = "mage" };
		SaveGame restored = SaveService.DeserializeCurrentVersion(JsonSerializer.Serialize(save));
		AssertBool(restored.Seed == save.Seed).IsTrue();
		AssertInt((int)restored.DungeonDepth).IsEqual(3);
		AssertInt((int)restored.LootRollsConsumed).IsEqual(17);
		AssertString(restored.CharacterClassId).IsEqual("mage");
	}
}