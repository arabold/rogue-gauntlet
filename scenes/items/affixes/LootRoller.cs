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
			Pool.RollAffixes(instance.ValidSlots, instance.Rarity, rng));
		return instance;
	}
}
