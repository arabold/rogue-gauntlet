namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tier-gating tests for <see cref="Affix"/>/<see cref="AffixPool"/>: high-tier affixes must
/// never roll on a lower-tier base item, independent of rolled rarity. Needs the Godot runtime
/// because <see cref="Affix"/>/<see cref="AffixPool"/> are Resources and the RNG is Godot's
/// <c>RandomNumberGenerator</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AffixTierGatingTest
{
	private static Affix MakeAffix(string name, int minTier, EquipableItemRarity minRarity = EquipableItemRarity.Common)
	{
		return new Affix
		{
			NameFragment = name,
			MinTier = minTier,
			MinRarity = minRarity,
			Weight = 1f,
			Modifiers = [new AffixModifierRange { Stat = StatType.Armor, Op = ModifierOp.Flat, MinValue = 1, MaxValue = 1 }],
		};
	}

	[TestCase]
	public void CanRollOnRejectsAffixesAboveTheItemTier()
	{
		Affix highTier = MakeAffix("Merciless", minTier: 4);

		AssertBool(highTier.CanRollOn(ValidSlots.WeaponHand, EquipableItemRarity.Legendary, tier: 1)).IsFalse();
		AssertBool(highTier.CanRollOn(ValidSlots.WeaponHand, EquipableItemRarity.Legendary, tier: 4)).IsTrue();
	}

	[TestCase]
	public void ZeroMinTierAllowsAnyTier()
	{
		Affix anyTier = MakeAffix("Precise", minTier: 0);

		AssertBool(anyTier.CanRollOn(ValidSlots.WeaponHand, EquipableItemRarity.Common, tier: 1)).IsTrue();
	}

	[TestCase]
	public void RollAffixesNeverStampsAnAffixAboveTheGivenTier()
	{
		var pool = new AffixPool
		{
			Affixes = [MakeAffix("LowTier", minTier: 1), MakeAffix("HighTier", minTier: 5)],
			RollCounts = [new AffixRollCount { Rarity = EquipableItemRarity.Rare, MinAffixes = 2, MaxAffixes = 2 }],
		};
		var rng = new RandomNumberGenerator();
		rng.Seed = 42;

		for (int i = 0; i < 100; i++)
		{
			RolledAffix[] rolled = pool.RollAffixes(ValidSlots.WeaponHand, EquipableItemRarity.Rare, tier: 1, rng);
			foreach (RolledAffix affix in rolled)
			{
				AssertBool(affix.NameFragment != "HighTier").IsTrue();
			}
		}
	}
}
