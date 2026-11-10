namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The authored character catalog: every class is complete and starts in the first tier, lookup-by-id behaves
/// (including the default GameSession falls back to), and applying a class derives the player's pools and damage from
/// its attributes without mutating the definition. Needs the Godot runtime to load the .tres resources.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class CharacterCatalogTest
{
	[TestCase]
	public void FindByIdResolvesKnownAndRejectsUnknownIds()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		AssertObject(catalog.DefaultClass).IsSame(catalog.Classes[0]);
		AssertObject(catalog.FindById(catalog.Classes[^1].Id)).IsSame(catalog.Classes[^1]);
		AssertObject(catalog.FindById("necromancer")).IsNull();
		AssertObject(catalog.FindById("")).IsNull();
		AssertObject(catalog.FindById(null)).IsNull();
	}

	[TestCase]
	public void EveryClassIsCompletelyAuthored()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();

		AssertInt(catalog.Classes.Count).IsGreater(0);
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
	public void StartingEquipmentStaysInTheFirstTier()
	{
		foreach (CharacterClass characterClass in CharacterCatalog.LoadDefault().Classes)
		{
			foreach (var slot in characterClass.StartingItems)
			{
				if (slot.Item is EquipableItem equipment)
				{
					AssertInt(equipment.Tier).OverrideFailureMessage($"{characterClass.Id} starts with T{equipment.Tier} {equipment.Name}").IsEqual(1);
				}
			}
		}
	}

	/// <summary>A made-up class, so balance changes to the authored classes never break the derivation rules.</summary>
	[TestCase]
	public void ApplyingAClassDerivesFullPoolsAndDamageFromItsAttributes()
	{
		var profile = new StatProfile { HealthPerVitality = 4, ManaPerIntelligence = 3, DamagePerStrength = 0.5f, DamagePerIntelligence = 0.25f };
		var hero = new CharacterClass { StatProfile = profile, BaseStrength = 6, BaseVitality = 10, BaseIntelligence = 8 };
		var stats = new PlayerStats { BaseMaxHealth = 50, BaseMaxMana = 20, BaseMinDamage = 1, BaseMaxDamage = 3, Health = 1, Mana = 0 };

		hero.ApplyToStats(stats);

		AssertObject(stats.Profile).IsSame(profile);
		AssertFloat(stats.MaxHealth).IsEqual(50f + 10 * 4);
		AssertFloat(stats.MaxMana).IsEqual(20f + 8 * 3);
		// Both pools start full.
		AssertFloat(stats.Health).IsEqual(stats.MaxHealth);
		AssertFloat(stats.Mana).IsEqual(stats.MaxMana);
		// Strength and Intelligence both feed the damage range: 6 * 0.5 + 8 * 0.25 = 5.
		AssertFloat(stats.MinDamage).IsEqual(1f + 5);
		AssertFloat(stats.MaxDamage).IsEqual(3f + 5);
		// Item bonuses add on top of the derived pool.
		stats.AddModifier(this, new StatModifier { Stat = StatType.MaxMana, Value = 10 });
		AssertFloat(stats.MaxMana).IsEqual(20f + 8 * 3 + 10);
	}

	[TestCase]
	public void StartingInventoryIsACopyOfTheDefinition()
	{
		CharacterCatalog catalog = CharacterCatalog.LoadDefault();
		CharacterClass first = catalog.Classes[0];

		Inventory inventory = first.CreateStartingInventory(20);

		AssertInt(inventory.Items.Count).IsEqual(first.StartingItems.Count);
		int authoredQuantity = first.StartingItems[0].Quantity;
		inventory.Items[0].Quantity = 99;
		AssertInt(first.StartingItems[0].Quantity).IsEqual(authoredQuantity);
	}
}