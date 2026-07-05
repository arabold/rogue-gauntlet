namespace RogueGauntlet.Tests;

using System.Text.Json;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Pure-logic tests for the v4 save schema's character-class field: pre-v4 saves (which
/// lack the key) must deserialize to the original Barbarian, and the chosen class must
/// survive a JSON round-trip. No Godot runtime needed.
/// </summary>
[TestSuite]
public class SaveGameCharacterClassTest
{
	[TestCase]
	public void CurrentVersionIsFour()
	{
		AssertInt(SaveGame.CurrentVersion).IsEqual(4);
	}

	[TestCase]
	public void PreV4SaveDefaultsToBarbarian()
	{
		SaveGame save = JsonSerializer.Deserialize<SaveGame>("{}");

		AssertString(save.CharacterClassId).IsEqual(SaveGame.DefaultCharacterClassId);
		AssertString(save.CharacterClassId).IsEqual("barbarian");
	}

	[TestCase]
	public void CharacterClassIdSurvivesRoundTrip()
	{
		var save = new SaveGame { CharacterClassId = "mage" };

		string json = JsonSerializer.Serialize(save);
		SaveGame restored = JsonSerializer.Deserialize<SaveGame>(json);

		AssertString(restored.CharacterClassId).IsEqual("mage");
	}
}
