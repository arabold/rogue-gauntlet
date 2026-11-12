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
		var profile = new StatProfile { HealthPercentPerVitality = 0.05f, ManaPerIntelligence = 3, PhysicalDamagePercentPerStrength = 0.1f };
		var hero = new CharacterClass { StatProfile = profile, BaseStrength = 6, BaseVitality = 14, BaseIntelligence = 8 };
		var stats = new PlayerStats { BaseMaxHealth = 50, BaseMaxMana = 20, BaseMinDamage = 1, BaseMaxDamage = 3, Health = 1, Mana = 0 };

		hero.ApplyToStats(stats);

		AssertObject(stats.Profile).IsSame(profile);
		// Vitality scales health by a share per point above 10: 4 points of 5% each.
		AssertFloat(stats.MaxHealth).IsEqualApprox(50f * 1.2f, 0.001f);
		AssertFloat(stats.MaxMana).IsEqual(20f + 8 * 3);
		// Both pools start full.
		AssertFloat(stats.Health).IsEqual(stats.MaxHealth);
		AssertFloat(stats.Mana).IsEqual(stats.MaxMana);
		// Strength below 10 shrinks the unarmed hit: 4 points of 10% each.
		AssertFloat(stats.MinDamage).IsEqualApprox(1f * 0.6f, 0.001f);
		AssertFloat(stats.MaxDamage).IsEqualApprox(3f * 0.6f, 0.001f);
		// Item bonuses add on top of the derived pool.
		stats.AddModifier(this, new StatModifier { Stat = StatType.MaxMana, Value = 10 });
		AssertFloat(stats.MaxMana).IsEqual(20f + 8 * 3 + 10);
	}

	/// <summary>Experience climbs the ladder: each level grows the class's attributes and max health without healing.</summary>
	[TestCase]
	public void ExperienceRaisesLevelsAttributesAndHealthUpToTheCap()
	{
		var profile = new StatProfile { HealthPercentPerVitality = 0.05f, VitalityPerLevel = 1, StrengthPerLevel = 0.5f };
		var stats = new PlayerStats { Profile = profile, BaseMaxHealth = 50, BaseVitality = 10, BaseStrength = 10,
			Leveling = new LevelingProfile { XpToNextLevel = [10, 20], HealthPerLevel = 5 } };
		stats.Health = stats.MaxHealth - 20;
		var levels = new System.Collections.Generic.List<int>();
		stats.LeveledUp += levels.Add;

		stats.AddXp(9);
		AssertInt(stats.XpLevel).IsEqual(1);
		stats.AddXp(100);

		AssertArray(levels.ToArray()).IsEqual(new[] { 2, 3 });
		AssertInt(stats.XpLevel).IsEqual(3);
		AssertFloat(stats.BaseVitality).IsEqual(12);
		AssertFloat(stats.BaseStrength).IsEqual(11);
		// Two levels add 10 base health, then 2 Vitality points scale it by 10%.
		AssertFloat(stats.MaxHealth).IsEqualApprox(60f * 1.1f, 0.001f);
		AssertFloat(stats.MaxHealth - stats.Health).IsEqualApprox(20, 0.001f);
		AssertObject(stats.LevelProgress).IsEqual((0, 0));
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