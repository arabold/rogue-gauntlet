using Godot;
using System.Linq;

/// <summary>
/// Turns a shared equipable definition into a rolled loot instance: a duplicate carrying a quality from the depth's odds,
/// its Poor or Superior adjective, a base-stat scale inside the quality's band and, for Magic and Rare, affixes from the
/// definition's <see cref="EquipmentRollTable"/>, then possibly a hidden curse. Everything is rolled here, once, at
/// creation; nothing rolls later.
/// Fixed creation copies authored state without randomizing it.
/// </summary>
public static class LootRoller
{
	/// <summary>
	/// Rolls a created instance from one RNG in a fixed order: quality, adjective, scale, affix count, affixes, curse, penalty,
	/// then the item's own intrinsic roll (a spell weapon's spell).
	/// A forced <paramref name="quality"/> or <paramref name="cursed"/> skips that draw, which the debug spawner uses.
	/// Harmful types are always cursed, Magic and Rare attributes start hidden, and every rolled item starts with an
	/// unknown curse status.
	/// </summary>
	public static void ApplyRoll(EquipableItem instance, uint depth, RandomNumberGenerator rng, ItemCreationPolicy policy,
		ItemQuality? quality = null, bool? cursed = null)
	{
		if (!instance.IsRuntimeInstance) { throw new System.InvalidOperationException("Cannot roll a shared definition."); }
		EquipmentRollTable table = instance.RollTable
			?? throw new System.InvalidOperationException($"{instance.Name} has no roll table.");
		QualityLadder ladder = policy.QualityLadder;
		instance.Quality = quality ?? ladder.Roll(depth, rng);
		instance.QualityAdjective = table.RollAdjective(instance.Quality, rng);
		instance.MagnitudeScale = ladder.RollScale(instance.Quality, rng);
		int count = ladder.RollAffixCount(instance.Quality, rng);
		instance.Affixes = new Godot.Collections.Array<RolledAffix>(
			count > 0 ? table.RollAffixes(depth, count, rng) : System.Array.Empty<RolledAffix>());
		instance.AttributesKnown = instance.Quality < ItemQuality.Magic;
		bool isCursed = cursed ?? (instance.AlwaysCursed || rng.Randf() < policy.CurseChance);
		instance.CursePenalty = isCursed ? RollPenalty(depth, rng, policy.CursePenalties) : [];
		instance.CurseStatusKnown = false;
		instance.RollIntrinsic(rng);
	}

	/// <summary>The modifiers of one curse penalty eligible at <paramref name="depth"/>; a curse always has one.</summary>
	private static StatModifier[] RollPenalty(uint depth, RandomNumberGenerator rng, Affix[] penalties)
	{
		Affix[] eligible = (penalties ?? []).Where(penalty => penalty != null && penalty.MinDepth <= depth).ToArray();
		if (eligible.Length == 0) { throw new System.InvalidOperationException($"No curse penalty is eligible at depth {depth}."); }
		return Affix.PickWeighted(eligible, rng).RollModifiers(rng);
	}

	/// <summary>True when enchanting can still add an affix: the item has a roll table and fewer than Rare's maximum.</summary>
	public static bool CanEnchant(EquipableItem item, QualityLadder ladder) =>
		item?.RollTable != null && (item.Affixes?.Count ?? 0) < (ladder?.Level(ItemQuality.Rare)?.MaxAffixes ?? 0);

	/// <summary>
	/// Scroll of Enchantment: adds one affix from the item's table eligible at <paramref name="depth"/>, never stacking a
	/// family it already holds. The quality becomes Magic or Rare to match the new affix count, so the Poor or Superior adjective
	/// goes while the base scale stays; the item's attributes become known and any curse is lifted.
	/// Spending a found scroll is not depth-fenced beyond affix eligibility. Returns false when nothing could be added.
	/// </summary>
	public static bool Enchant(EquipableItem item, uint depth, RandomNumberGenerator rng, QualityLadder ladder)
	{
		if (item == null || ladder == null || rng == null || !CanEnchant(item, ladder))
		{
			return false;
		}

		if (!item.IsRuntimeInstance) { throw new System.InvalidOperationException("Cannot enchant a shared definition."); }

		var affixes = new Godot.Collections.Array<RolledAffix>(item.Affixes ?? new Godot.Collections.Array<RolledAffix>());
		RolledAffix added = item.RollTable.RollAdditional(depth, affixes, rng);
		if (added == null)
		{
			return false;
		}

		affixes.Add(added);
		item.Affixes = affixes;
		item.QualityAdjective = "";
		item.RevealAttributes();
		item.LiftCurse();
		item.Quality = affixes.Count > (ladder.Level(ItemQuality.Magic)?.MaxAffixes ?? 0) ? ItemQuality.Rare : ItemQuality.Magic;
		return true;
	}
}