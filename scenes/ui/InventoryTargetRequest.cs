using System;

/// <summary>
/// One "choose an item" interaction driven by <see cref="InventoryPanel"/>: which slots
/// are eligible, what the banner says, and what happens on confirm or cancel. Used by
/// scroll effects that need the player to pick a target (identify, enchant) rather than
/// acting immediately on read.
/// </summary>
public sealed class InventoryTargetRequest
{
	public string Prompt { get; init; } = "Choose an item";
	public Func<InventoryItemSlot, bool> IsEligible { get; init; } = _ => false;
	public Action<InventoryItemSlot> Confirmed { get; init; }
	public Action Cancelled { get; init; }
}
