using Godot;

/// <summary>
/// Turns a shared equipable definition into a rolled loot instance: a duplicate carrying a
/// depth-scaled rarity and a set of rolled affixes. Non-equipables, a missing affix pool, or
/// a null RNG return the item unchanged, which keeps fixed/authored drops and the editor
/// working. The affix pool is loaded once from a known path, mirroring the identity catalog.
/// </summary>
public static class LootRoller
{
	private const string PoolPath = "res://scenes/items/affixes/affix_pool.tres";

	private static AffixPool _pool;
	private static AffixPool Pool => _pool ??= ResourceLoader.Load<AffixPool>(PoolPath);

	public static Item Roll(Item item, uint depth, RandomNumberGenerator rng)
	{
		if (item is not EquipableItem definition || Pool == null || rng == null)
		{
			return item;
		}

		var instance = (EquipableItem)definition.Duplicate(true);
		instance.SourceDefinitionPath = definition.ResourcePath;
		instance.Rarity = Pool.RollRarity(depth, rng);
		instance.Affixes = new Godot.Collections.Array<RolledAffix>(
			Pool.RollAffixes(instance.ValidSlots, instance.Rarity, instance.Tier, rng));
		return instance;
	}

	/// <summary>
	/// The item itself if it is already a rolled instance (has a source definition path),
	/// else a duplicate stamped with its source path. Used by the Enchant scroll so
	/// enchanting a shared definition never mutates the authored <c>.tres</c>.
	/// </summary>
	public static EquipableItem EnsureInstance(EquipableItem item)
	{
		if (!string.IsNullOrEmpty(item.SourceDefinitionPath))
		{
			return item;
		}

		var instance = (EquipableItem)item.Duplicate(true);
		instance.SourceDefinitionPath = item.ResourcePath;
		return instance;
	}

	/// <summary>
	/// Scroll of Enchantment: bumps rarity one step (capped at Legendary) and appends one
	/// newly rolled affix eligible for the item's slots/tier, skipping name fragments the
	/// item already carries so it never stacks the same affix twice.
	/// </summary>
	public static void Enchant(EquipableItem item, RandomNumberGenerator rng)
	{
		if (item == null || Pool == null || rng == null)
		{
			return;
		}

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

		RolledAffix newAffix = Pool.RollSingleAffix(item.ValidSlots, item.Rarity, item.Tier, existingNames, rng);
		if (newAffix == null)
		{
			return;
		}

		var affixes = new Godot.Collections.Array<RolledAffix>(item.Affixes ?? new Godot.Collections.Array<RolledAffix>());
		affixes.Add(newAffix);
		item.Affixes = affixes;
	}
}
