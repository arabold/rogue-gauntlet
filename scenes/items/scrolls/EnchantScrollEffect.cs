using Godot;

/// <summary>
/// Scroll of Enchantment: bumps the chosen equipable's rarity and adds one affix via
/// <see cref="LootRoller.Enchant"/>. Unequips the item first and re-equips it after, since
/// <see cref="EquipableItem.OnEquipped"/> registered its stat modifiers under the old item
/// instance and mutating in place would leave stale modifiers active.
/// </summary>
[GlobalClass]
public partial class EnchantScrollEffect : ScrollEffect
{
	public override bool RequiresTarget => true;

	public override bool IsValidTarget(Player player, InventoryItemSlot slot)
	{
		return slot?.Item is EquipableItem;
	}

	public override void ApplyToTarget(Player player, InventoryItemSlot slot)
	{
		Inventory inventory = player.Inventory;
		EquipmentSlot? equippedIn = null;
		foreach (var pair in inventory.EquippedItems)
		{
			if (pair.Value == slot)
			{
				equippedIn = pair.Key;
				break;
			}
		}

		if (equippedIn.HasValue)
		{
			inventory.Unequip(equippedIn.Value);
		}

		EquipableItem instance = LootRoller.EnsureInstance((EquipableItem)slot.Item);
		LootRoller.Enchant(instance, GameSession.Instance?.CreateLootRng() ?? new RandomNumberGenerator());
		slot.Item = instance;

		if (equippedIn.HasValue)
		{
			inventory.Equip(slot, equippedIn.Value);
		}
	}
}
