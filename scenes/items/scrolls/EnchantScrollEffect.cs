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
		return player?.GetAncestorOrNull<Level>() != null && slot?.Item is EquipableItem item && LootRoller.CanEnchant(item, QualityLadder);
	}

	public override void ApplyToTarget(Player player, InventoryItemSlot slot)
	{
		Level level = player?.GetAncestorOrNull<Level>();
		if (level == null || slot?.Item is not EquipableItem target || !LootRoller.CanEnchant(target, QualityLadder))
		{
			return;
		}

		// The floor being read on, not the session's next depth: a scroll read during a stair fade rolls this floor.
		player.Inventory.ChangeItem(slot, item => LootRoller.Enchant(item, level.Depth, level.Run.CreateLootRng(level.Seed), QualityLadder));
	}
}