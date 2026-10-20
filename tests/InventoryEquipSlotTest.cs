namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Regression test for the ring-slot equip bug: <see cref="Inventory.Equip(InventoryItemSlot)"/>'s
/// empty-slot search used to stop at <see cref="EquipmentSlot.Neck"/> (32), so it never probed
/// <see cref="EquipmentSlot.LeftRing"/> (64) or <see cref="EquipmentSlot.RightRing"/> (128) —
/// a second ring always evicted the first from LeftRing instead of filling RightRing. Needs the
/// Godot runtime because <see cref="Inventory"/>/<see cref="EquipableItem"/> are Resources.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InventoryEquipSlotTest
{
	[TestCase]
	public void EquippingTwoRingsFillsBothRingSlots()
	{
		var inventory = new Inventory();
		var ringA = ResourceLoader.Load<EquipableItem>("res://scenes/items/jewelry/ring_strength.tres");
		var ringB = ResourceLoader.Load<EquipableItem>("res://scenes/items/jewelry/ring_dexterity.tres");
		var slotA = new InventoryItemSlot { Item = ringA, Quantity = 1 };
		var slotB = new InventoryItemSlot { Item = ringB, Quantity = 1 };
		inventory.Items.Add(slotA);
		inventory.Items.Add(slotB);

		inventory.Equip(slotA);
		inventory.Equip(slotB);

		AssertObject(inventory.EquippedItems[EquipmentSlot.LeftRing]).IsEqual(slotA);
		AssertObject(inventory.EquippedItems[EquipmentSlot.RightRing]).IsEqual(slotB);
	}

	[TestCase]
	public async Task OpeningEquipmentPanelShowsTwoHandedWeaponInBothHands()
	{
		ISceneRunner runner = ISceneRunner.Load("res://scenes/ui/equipment_panel.tscn", true);
		await runner.SimulateFrames(1);
		var panel = (EquipmentPanel)runner.Scene();
		var inventory = new Inventory();
		var weapon = (Weapon)ItemFactory.Create(GD.Load<Item>("res://scenes/items/weapons/crossbow_common.tres"));
		inventory.TryAdd(weapon);
		inventory.Equip(inventory.Items[0]);

		panel.Initialize(inventory);
		AssertObject(panel.WeaponPanel.GetNode<ItemSlotPanel>("%ItemSlotPanel").Slot.Item).IsEqual(weapon);
		AssertObject(panel.ShieldPanel.GetNode<ItemSlotPanel>("%ItemSlotPanel").Slot.Item).IsEqual(weapon);
		// The mirrored preview must not create a second equipment owner.
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();

		panel.Initialize(null);
		panel.Initialize(inventory);
		AssertObject(panel.ShieldPanel.GetNode<ItemSlotPanel>("%ItemSlotPanel").Slot.Item).IsEqual(weapon);
		inventory.Unequip(EquipmentSlot.WeaponHand);
		AssertObject(panel.WeaponPanel.GetNode<ItemSlotPanel>("%ItemSlotPanel").Slot.Item).IsNull();
		AssertObject(panel.ShieldPanel.GetNode<ItemSlotPanel>("%ItemSlotPanel").Slot.Item).IsNull();
	}

}