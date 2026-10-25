using Godot;
using System;
using System.Linq;

/// <summary>Spends one key of the lock's <see cref="DoorLockDefinition.RequiredKey"/> definition.</summary>
[Tool]
[GlobalClass]
public partial class KeyUnlockMethod : UnlockMethod
{
	public override string OperationId => DoorOperation.KeyOpen;
	public override InventoryItemSlot FindCost(DoorLockDefinition lockDefinition, Inventory inventory)
	{
		string key = lockDefinition.RequiredKey?.DefinitionPath;
		return string.IsNullOrEmpty(key) ? null
			: inventory?.Items.FirstOrDefault(s => s.Quantity > 0 && s.Item is KeyItem && s.Item.DefinitionPath == key);
	}
	public override void Validate(DoorLockDefinition lockDefinition)
	{
		base.Validate(lockDefinition);
		if (lockDefinition.RequiredKey == null) throw new InvalidOperationException($"Key unlock needs a required key: {lockDefinition.ResourcePath}");
	}
}