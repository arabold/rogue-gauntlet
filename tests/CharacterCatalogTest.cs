namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Validates the authored character catalog: the four classes load complete and in order,
/// lookup-by-id behaves (including the fallback contract GameSession relies on), and the
/// class application helpers derive the expected stats without mutating the definitions.
/// Needs the Godot runtime to load the .tres resources.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class CharacterCatalogTest
{
	[TestCase]
	public void CatalogContainsTheFourClassesInOrder()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		AssertObject(catalog).IsNotNull();
		AssertInt(catalog.Classes.Count).IsEqual(4);
		AssertString(catalog.Classes[0].Id).IsEqual("barbarian");
		AssertString(catalog.Classes[1].Id).IsEqual("knight");
		AssertString(catalog.Classes[2].Id).IsEqual("rogue");
		AssertString(catalog.Classes[3].Id).IsEqual("mage");
		AssertString(catalog.DefaultClass.Id).IsEqual("barbarian");
	}

	[TestCase]
	public void FindByIdResolvesKnownAndRejectsUnknownIds()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		AssertString(catalog.FindById("rogue").DisplayName).IsEqual("Rogue");
		AssertObject(catalog.FindById("necromancer")).IsNull();
		AssertObject(catalog.FindById("")).IsNull();
		AssertObject(catalog.FindById(null)).IsNull();
	}

	[TestCase]
	public void EveryClassIsCompletelyAuthored()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		foreach (CharacterClass characterClass in catalog.Classes)
		{
			AssertString(characterClass.Id).IsNotEmpty();
			AssertString(characterClass.DisplayName).IsNotEmpty();
			AssertString(characterClass.Description).IsNotEmpty();
			AssertObject(characterClass.CharacterScene).IsNotNull();
			AssertObject(characterClass.StatProfile).IsNotNull();
			AssertInt(characterClass.StartingItems.Count).IsGreater(0);
			AssertString(characterClass.PreviewAnimation).IsNotEmpty();
		}
	}

	[TestCase]
	public void ApplyToStatsDerivesClassHealth()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();
		CharacterClass knight = catalog.FindById("knight");
		var stats = new PlayerStats();

		knight.ApplyToStats(stats);

		// BaseMaxHealth 50 + Vitality 14 * HealthPerVitality 5 = 120, refilled on apply.
		AssertFloat(stats.MaxHealth).IsEqual(120f);
		AssertFloat(stats.Health).IsEqual(stats.MaxHealth);
		AssertFloat(stats.BaseStrength).IsEqual(12f);
		AssertObject(stats.Profile).IsSame(knight.StatProfile);
	}

	[TestCase]
	public void MageProfileScalesDamageWithIntelligence()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();
		CharacterClass mage = catalog.FindById("mage");
		var stats = new PlayerStats();

		mage.ApplyToStats(stats);

		// Damage derivation: STR 6 * 0.05 + INT 16 * 0.2 = 3.5 on top of the base values.
		AssertFloat(stats.MinDamage).IsEqual(3.5f);
		AssertFloat(stats.MaxDamage).IsEqual(5.5f);
	}

	[TestCase]
	public void StartingInventoryIsACopyOfTheDefinition()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();
		CharacterClass barbarian = catalog.FindById("barbarian");

		Inventory inventory = barbarian.CreateStartingInventory(20);

		AssertInt(inventory.Items.Count).IsEqual(barbarian.StartingItems.Count);
		int authoredQuantity = barbarian.StartingItems[0].Quantity;
		inventory.Items[0].Quantity = 99;
		AssertInt(barbarian.StartingItems[0].Quantity).IsEqual(authoredQuantity);
	}
}
