namespace RogueGauntlet.Tests;

using System.Text.Json;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Pure-logic tests for character selection in the current save format.
/// No backward-compatibility behavior is required during development.
/// </summary>
[TestSuite]
public class SaveGameCharacterClassTest
{
	[TestCase]
	public void CharacterClassIdSurvivesRoundTrip()
	{
		var save = new SaveGame { CharacterClassId = "mage" };

		string json = JsonSerializer.Serialize(save);
		SaveGame restored = SaveService.DeserializeCurrentVersion(json);

		AssertString(restored.CharacterClassId).IsEqual("mage");
	}
}