using Godot;

/// <summary>
/// Scroll of Enchantment: adds one affix eligible at the current depth to the chosen equipable via
/// <see cref="LootRoller.Enchant"/>, making it Magic or Rare and lifting any curse. Runs through
/// <see cref="Inventory.ChangeItem"/>, so a worn item's stats are re-registered after the change.
/// </summary>
[Tool]
[GlobalClass]
public partial class EnchantScrollEffect : ScrollEffect
{
	/// <summary>Quality rules: how many affixes Magic holds before an item turns Rare, and Rare's maximum.</summary>
	[Export] public QualityLadder QualityLadder { get; set; }

	public override bool RequiresTarget => true;

	public override bool IsValidTarget(Player player, InventoryItemSlot slot)
	{
		return slot?.Item is EquipableItem item && LootRoller.CanEnchant(item, QualityLadder);
	}

	public override void ApplyToTarget(Player player, InventoryItemSlot slot)
	{
		if (player == null || slot?.Item is not EquipableItem target || !LootRoller.CanEnchant(target, QualityLadder))
		{
			return;
		}

		player.Inventory.ChangeItem(slot, item => LootRoller.Enchant(item, GameSession.Instance?.ActiveDungeonDepth ?? 1,
			GameSession.Instance?.CreateLootRng() ?? new RandomNumberGenerator(), QualityLadder));
	}
}