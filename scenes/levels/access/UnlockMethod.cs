using Godot;
using System;

/// <summary>
/// One authored way to open a mechanical lock, listed on <see cref="DoorLockDefinition.Methods"/>. Each method owns
/// its prompt, timing, permission and the inventory it spends, so a new way to unlock is a new resource rather than
/// another branch in the door.
/// </summary>
[Tool]
[GlobalClass]
public abstract partial class UnlockMethod : Resource
{
	[Export] public string Label { get; set; } = "Unlock";
	[Export(PropertyHint.Range, "0.1,60")] public float Duration { get; set; } = 1;
	/// <summary>Interaction operation id; unique within one lock.</summary>
	public abstract string OperationId { get; }
	/// <summary>Required in addition to <see cref="InteractionPermission.OpenDoors"/>.</summary>
	public virtual InteractionPermission Permission => InteractionPermission.OpenDoors;
	/// <summary>The stack this method would spend, or null when the inventory cannot pay.</summary>
	public abstract InventoryItemSlot FindCost(DoorLockDefinition lockDefinition, Inventory inventory);
	public virtual void Validate(DoorLockDefinition lockDefinition)
	{
		if (string.IsNullOrWhiteSpace(Label) || !float.IsFinite(Duration) || Duration <= 0)
			throw new InvalidOperationException($"Invalid unlock method: {ResourcePath}");
	}
}