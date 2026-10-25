using Godot;
using System.Linq;

/// <summary>Spends one lockpick; slower than a key and limited to actors allowed to pick mechanical locks.</summary>
[Tool]
[GlobalClass]
public partial class LockpickUnlockMethod : UnlockMethod
{
	public LockpickUnlockMethod()
	{
		Label = "Pick lock";
		Duration = 5;
	}
	public override string OperationId => DoorOperation.PickOpen;
	public override InteractionPermission Permission => InteractionPermission.PickMechanicalLocks;
	public override InventoryItemSlot FindCost(DoorLockDefinition lockDefinition, Inventory inventory) =>
		inventory?.Items.FirstOrDefault(s => s.Quantity > 0 && s.Item is LockpickItem);
}