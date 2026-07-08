using Godot;

/// <summary>The classic "scroll of identify": reveals a chosen unidentified item's true type.</summary>
[GlobalClass]
public partial class IdentifyScrollEffect : ScrollEffect
{
	public override bool RequiresTarget => true;

	public override bool IsValidTarget(Player player, InventoryItemSlot slot)
	{
		return slot?.Item is IIdentifiable { HasIdentity: true } target
			&& GameSession.Instance?.Identification.IsIdentified(target) == false;
	}

	public override void ApplyToTarget(Player player, InventoryItemSlot slot)
	{
		if (slot == null)
		{
			return;
		}

		GameSession.Instance?.IdentifyItemType(slot.Item);
	}
}
