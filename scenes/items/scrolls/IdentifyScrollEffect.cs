using Godot;

/// <summary>
/// The classic "scroll of identify": reveals a chosen item's hidden type (run-wide), its own hidden attributes and
/// whether it is cursed.
/// </summary>
[Tool]
[GlobalClass]
public partial class IdentifyScrollEffect : ScrollEffect
{
	public override bool RequiresTarget => true;

	public override bool IsValidTarget(Player player, InventoryItemSlot slot)
	{
		return slot?.Item != null && !ItemIdentity.IsFullyIdentified(slot.Item);
	}

	public override void ApplyToTarget(Player player, InventoryItemSlot slot)
	{
		if (slot == null)
		{
			return;
		}

		player?.GetAncestorOrNull<Level>()?.Run.IdentifyItemType(slot.Item);
		if (slot.Item is EquipableItem equipment)
		{
			equipment.RevealAttributes();
			equipment.RevealCurseStatus();
		}
	}
}
