using Godot;

/// <summary>
/// Scroll of Remove Curse: uncurses one chosen piece of equipment, worn or in the backpack. Any item not known to be
/// uncursed is a valid target, cursed or not, and the outcome is the same either way: the item ends up known as
/// uncursed. So choosing a target and reading the scroll never reveal whether it had been cursed.
/// </summary>
[Tool]
[GlobalClass]
public partial class RemoveCurseScrollEffect : ScrollEffect
{
	public override bool RequiresTarget => true;

	public override bool IsValidTarget(Player player, InventoryItemSlot slot)
	{
		return slot?.Item is EquipableItem item && (item.IsCursed || !item.CurseStatusKnown);
	}

	public override void ApplyToTarget(Player player, InventoryItemSlot slot)
	{
		if (player?.Inventory == null || slot?.Item is not EquipableItem target || !IsValidTarget(player, slot))
		{
			return;
		}

		player.Inventory.ChangeItem(slot, item => item.LiftCurse());
	}
}
