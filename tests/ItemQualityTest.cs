namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Item qualities: introduction depths and odds of the authored <see cref="QualityLadder"/>, base-stat bands, rounding
/// of scaled values, what creation rolls, the naming grammar, and the base-tier depth windows of every loot table.
/// Needs the Godot runtime for Resources and <c>RandomNumberGenerator</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ItemQualityTest
{
	private const string LadderPath = "res://scenes/items/quality/quality_ladder.tres";

	/// <summary>Base-tier depth windows (MaxDepth 0 is open-ended); see docs/stats-and-itemization.md.</summary>
	private static TierDepthBands TierBands => TestFactories.Load<TierDepthBands>("res://scenes/items/quality/tier_depth_bands.tres");

	private static QualityLadder Ladder => TestFactories.Load<QualityLadder>(LadderPath);
	private static ItemCreationPolicy Policy => TestFactories.Load<ItemCreationPolicy>("res://scenes/items/dungeon_item_policy.tres");

	private static T Copy<T>(string path) where T : Item => (T)ItemFactory.CopyDefinition(TestFactories.Load<Item>(path));

	[TestCase]
	public void AuthoredLadderDefinesEveryQualityOnce()
	{
		AssertArray(Ladder.Validate()).IsEmpty();
	}

	[TestCase]
	public void WeightStartsAtTheIntroductionDepthAndGrowsFromThere()
	{
		var level = new QualityLevel { MinDepth = 5, StartWeight = 1.5f, WeightPerDepth = 0.4f };

		AssertFloat(level.WeightAt(4)).IsEqual(0f);
		AssertFloat(level.WeightAt(5)).IsEqualApprox(1.5f, 0.001f);
		AssertFloat(level.WeightAt(15)).IsEqualApprox(5.5f, 0.001f);
		AssertFloat(new QualityLevel { StartWeight = 15f, WeightPerDepth = -2f }.WeightAt(20)).IsEqual(0f);
	}

	[TestCase]
	public void MagicAndRareArriveAtTheirIntroductionDepthsAndLowerQualitiesNeverVanish()
	{
		var rng = new RandomNumberGenerator { Seed = 11 };
		var seen = new HashSet<(uint Depth, ItemQuality Quality)>();
		for (uint depth = 1; depth <= 24; depth++)
		{
			for (int i = 0; i < 1000; i++) { seen.Add((depth, Ladder.Roll(depth, rng))); }
		}

		AssertBool(seen.Contains((1, ItemQuality.Magic))).IsFalse();
		AssertBool(seen.Any(s => s.Quality == ItemQuality.Rare && s.Depth < 5)).IsFalse();
		AssertBool(seen.Contains((12, ItemQuality.Magic)) && seen.Contains((12, ItemQuality.Rare))).IsTrue();
		AssertBool(seen.Contains((24, ItemQuality.Normal)) && seen.Contains((24, ItemQuality.Poor))).IsTrue();
	}

	[TestCase]
	public void CreationRollsAdjectivesAndAffixesThatMatchTheQuality()
	{
		ItemCreationPolicy policy = Policy;
		string[] paths = ["res://scenes/items/weapons/broadsword_common.tres", "res://scenes/items/armor/shield_common.tres",
			"res://scenes/items/jewelry/ring/ring_strength.tres"];
		Item[] definitions = paths.Select(path => TestFactories.Load<Item>(path)).ToArray();
		using var rng = GenerationRandom.Create(42, "quality-creation");
		for (int i = 0; i < 600; i++)
		{
			var item = (EquipableItem)ItemFactory.Create(definitions[i % definitions.Length], policy, 16, rng);
			EquipmentRollTable table = item.RollTable;
			string[] words = item.Quality switch
			{
				ItemQuality.Poor => table.PoorAdjectives,
				ItemQuality.Superior => table.SuperiorAdjectives,
				_ => [""],
			};
			AssertBool(words.Contains(item.QualityAdjective)).IsTrue();
			AssertBool(item.AttributesKnown).IsEqual(item.Quality < ItemQuality.Magic);
			QualityLevel level = Ladder.Level(item.Quality);
			AssertInt(item.Affixes.Count).IsBetween(level.MinAffixes, level.MaxAffixes);
			AssertInt(item.Affixes.Select(affix => affix.Group).Distinct().Count()).IsEqual(item.Affixes.Count);
			AssertBool(item.Affixes.All(affix => table.Prefixes.Concat(table.Suffixes).Any(entry => entry.NameFragment == affix.NameFragment))).IsTrue();
		}
	}

	[TestCase]
	public void ScalesAndAffixCountsStayInsideTheirBands()
	{
		var rng = new RandomNumberGenerator { Seed = 3 };
		foreach (QualityLevel level in Ladder.Levels)
		{
			for (int i = 0; i < 200; i++)
			{
				AssertFloat(Ladder.RollScale(level.Quality, rng)).IsBetween(level.MinScale, level.MaxScale);
				AssertInt(Ladder.RollAffixCount(level.Quality, rng)).IsBetween(level.MinAffixes, level.MaxAffixes);
			}
		}
	}

	[TestCase]
	public void ScaledValuesRoundToWholeUnitsAndKeepAtLeastOne()
	{
		var weapon = new Weapon { DamageMin = 3, DamageMax = 5, MagnitudeScale = 1.5f };
		AssertFloat(weapon.ScaledDamageMin).IsEqual(5f);
		AssertFloat(weapon.ScaledDamageMax).IsEqual(8f);
		weapon = new Weapon { DamageMin = 1, DamageMax = 1, MagnitudeScale = 0.3f };
		AssertFloat(weapon.ScaledDamageMin).IsEqual(1f);
		AssertFloat(weapon.ScaledDamageMax).IsEqual(1f);
		AssertInt(new Armor { ArmorPoints = 3, MagnitudeScale = 1.6f }.ScaledArmorPoints).IsEqual(5);

		var frailty = Copy<IdentifiableEquipableItem>("res://scenes/items/jewelry/ring/ring_frailty.tres");
		frailty.MagnitudeScale = 1.6f;
		AssertFloat(frailty.StatModifiers.First().Value).IsEqual(-24f);

		var lethargy = Copy<IdentifiableEquipableItem>("res://scenes/items/jewelry/ring/ring_lethargy.tres");
		AssertFloat(lethargy.StatModifiers.First().Value).IsEqual(lethargy.IntrinsicModifiers[0].Value);
		lethargy.MagnitudeScale = 1.3f;
		AssertFloat(lethargy.StatModifiers.First().Value).IsEqualApprox(-0.2f, 0.0001f);
	}

	[TestCase]
	public void NamesFollowTheQualityAndJewelryShowsItsValue()
	{
		var sword = Copy<Weapon>("res://scenes/items/weapons/broadsword_common.tres");
		sword.Quality = ItemQuality.Poor;
		sword.QualityAdjective = "cracked";
		AssertString(sword.ComposeName(sword.Name)).IsEqual("Cracked Broadsword");
		sword.Quality = ItemQuality.Magic;
		sword.Affixes.Add(new RolledAffix { NameFragment = "of the Ox", Kind = AffixKind.Suffix });
		sword.Affixes.Add(new RolledAffix { NameFragment = "Vicious", Kind = AffixKind.Prefix });
		AssertString(sword.ComposeName(sword.Name)).IsEqual("Vicious Broadsword of the Ox");

		var ring = Copy<IdentifiableEquipableItem>("res://scenes/items/jewelry/ring/ring_strength.tres");
		AssertString(ring.ComposeName(ring.TrueName)).IsEqual("Ring of Strength +2");
		ring.Quality = ItemQuality.Superior;
		ring.QualityAdjective = "shiny";
		ring.MagnitudeScale = 1.5f;
		AssertString(ring.ComposeName(ring.TrueName)).IsEqual("Shiny Ring of Strength +3");
		var lethargy = Copy<IdentifiableEquipableItem>("res://scenes/items/jewelry/ring/ring_lethargy.tres");
		AssertString(lethargy.ComposeName(lethargy.TrueName)).IsEqual("Ring of Lethargy -15%");

		var service = new IdentificationService();
		service.Initialize(42);
		string descriptor = service.GetAppearance(ring).Descriptor;
		AssertString(ItemIdentity.ComposeDisplayName(ring, service)).IsEqual($"Shiny {descriptor} ring");
		ring.Quality = ItemQuality.Magic;
		ring.QualityAdjective = "";
		ring.MagnitudeScale = 1f;
		ring.Affixes.Add(new RolledAffix { NameFragment = "of the Owl", Kind = AffixKind.Suffix });
		ring.Affixes.Add(new RolledAffix { NameFragment = "Keen", Kind = AffixKind.Prefix });
		AssertString(ItemIdentity.ComposeDisplayName(ring, service)).IsEqual($"Magic {descriptor} ring");
		service.Identify(ring.TypeId);
		AssertString(ItemIdentity.ComposeDisplayName(ring, service)).IsEqual("Keen Ring of Strength +2");
	}

	[TestCase]
	public void MagicItemsHideTheirAttributesUntilIdentified()
	{
		var sword = Copy<Weapon>("res://scenes/items/weapons/broadsword_common.tres");
		var rng = new RandomNumberGenerator { Seed = 4 };
		LootRoller.ApplyRoll(sword, 12, rng, Policy, ItemQuality.Magic, cursed: false);
		var slot = new InventoryItemSlot { Item = sword };
		var identify = new IdentifyScrollEffect();

		AssertBool(sword.AttributesKnown).IsFalse();
		AssertString(ItemIdentity.ResolveDisplayName(sword)).IsEqual("Magic Broadsword");
		AssertBool(ItemIdentity.AreStatsKnown(sword)).IsFalse();
		AssertBool(identify.IsValidTarget(null, slot)).IsTrue();

		sword.RevealAttributes(); // what wearing it does
		AssertString(ItemIdentity.ResolveDisplayName(sword)).IsEqual(sword.ComposeName(sword.Name));
		AssertString(ItemIdentity.ResolveDisplayName(sword)).IsNotEqual("Magic Broadsword");
		AssertBool(identify.IsValidTarget(null, slot)).IsTrue(); // its curse status is still unknown
		identify.ApplyToTarget(null, slot);
		AssertBool(sword.CurseStatusKnown).IsTrue();
		AssertBool(identify.IsValidTarget(null, slot)).IsFalse();
	}

	[TestCase]
	public void JewelryCombinesTypeAndItemKnowledge()
	{
		var ring = Copy<IdentifiableEquipableItem>("res://scenes/items/jewelry/ring/ring_strength.tres");
		var rng = new RandomNumberGenerator { Seed = 8 };
		LootRoller.ApplyRoll(ring, 12, rng, Policy, ItemQuality.Magic, cursed: false);
		var service = new IdentificationService();
		service.Initialize(42);
		string descriptor = service.GetAppearance(ring).Descriptor;

		AssertString(ItemIdentity.ComposeDisplayName(ring, service)).IsEqual($"Magic {descriptor} ring");
		service.Identify(ring.TypeId);
		AssertString(ItemIdentity.ComposeDisplayName(ring, service)).IsEqual("Magic Ring of Strength");
		ring.RevealAttributes();
		AssertBool(ItemIdentity.ComposeDisplayName(ring, service).EndsWith("Ring of Strength +2")).IsTrue();
	}

	[TestCase]
	public void LootTablesFenceBaseTiersByDepth()
	{
		foreach (string path in Directory.EnumerateFiles(ProjectSettings.GlobalizePath("res://scenes/items/loot"), "*.tres"))
		{
			var table = TestFactories.Load<LootTable>(ProjectSettings.LocalizePath(path));
			foreach (LootTableItem entry in table.Entries)
			{
				if (entry.Item is IdentifiableEquipableItem)
				{
					AssertInt((int)entry.MinDepth).IsEqual(1);
				}
				else if (entry.Item is EquipableItem equipment)
				{
					Vector2I band = TierBands.For(equipment.Tier);
					AssertBool(entry.MinDepth == band.X && entry.MaxDepth == band.Y)
						.OverrideFailureMessage($"{entry.Item.ResourcePath}: T{equipment.Tier} drops {entry.MinDepth}-{entry.MaxDepth}").IsTrue();
				}
			}

			var rng = new RandomNumberGenerator { Seed = 19 };
			for (uint depth = 1; depth <= 24; depth++)
			{
				for (int i = 0; i < 300; i++)
				{
					if (table.PickEntry(depth, rng)?.Item is EquipableItem picked)
					{
						AssertBool(depth >= TierBands.For(picked.Tier).X).IsTrue();
					}
				}
			}
		}
	}
}
