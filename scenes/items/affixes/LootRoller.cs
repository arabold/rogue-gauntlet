using Godot;

/// <summary>
/// Turns a shared equipable definition into a rolled loot instance: a duplicate carrying a
/// depth-scaled rarity and a set of rolled affixes. The caller supplies an authored affix pool and the RNG.
/// Fixed creation copies authored state without randomizing it.
/// </summary>
public static class LootRoller
{
	public static void ApplyRoll(EquipableItem instance, uint depth, RandomNumberGenerator rng, AffixPool pool)
	{
		if (!instance.IsRuntimeInstance) { throw new System.InvalidOperationException("Cannot roll a shared definition."); }
		instance.Rarity = pool.RollRarity(depth, rng);
		instance.Affixes = new Godot.Collections.Array<RolledAffix>(
			pool.RollAffixes(instance.ValidSlots, instance.Rarity, instance.Tier, rng));
	}

	/// <summary>
	/// The item itself if it is already a rolled instance (has a source definition path),
	/// else a duplicate stamped with its source path. Used by the Enchant scroll so
	/// enchanting a shared definition never mutates the authored <c>.tres</c>.
	/// </summary>
	public static EquipableItem EnsureInstance(EquipableItem item)
	{
		if (item == null)
		{
			return null;
		}

		if (item.IsRuntimeInstance)
		{
			return item;
		}

		return (EquipableItem)ItemFactory.CopyDefinition(item);
	}

	/// <summary>
	/// Scroll of Enchantment: bumps rarity one step (capped at Legendary) and appends one
	/// newly rolled affix eligible for the item's slots/tier, skipping name fragments the
	/// item already carries so it never stacks the same affix twice.
	/// </summary>
	public static void Enchant(EquipableItem item, RandomNumberGenerator rng, AffixPool pool)
	{
		if (item == null || pool == null || rng == null)
		{
			return;
		}

		if (!item.IsRuntimeInstance) { throw new System.InvalidOperationException("Cannot enchant a shared definition."); }

		if (item.Rarity < EquipableItemRarity.Legendary)
		{
			item.Rarity++;
		}

		var existingNames = new System.Collections.Generic.List<string>();
		foreach (RolledAffix affix in item.Affixes ?? new Godot.Collections.Array<RolledAffix>())
		{
			if (!string.IsNullOrEmpty(affix?.NameFragment))
			{
				existingNames.Add(affix.NameFragment);
			}
		}

		RolledAffix newAffix = pool.RollSingleAffix(item.ValidSlots, item.Rarity, item.Tier, existingNames, rng);
		if (newAffix == null)
		{
			return;
		}

		var affixes = new Godot.Collections.Array<RolledAffix>(item.Affixes ?? new Godot.Collections.Array<RolledAffix>());
		affixes.Add(newAffix);
		item.Affixes = affixes;
	}
}