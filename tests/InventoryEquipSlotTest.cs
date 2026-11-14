namespace RogueGauntlet.Tests;

using System.Linq;
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
		var ringA = TestFactories.Load<EquipableItem>("res://scenes/items/jewelry/ring/ring_strength_1.tres");
		var ringB = TestFactories.Load<EquipableItem>("res://scenes/items/jewelry/ring/ring_dexterity_1.tres");
		var slotA = new InventoryItemSlot { Item = ringA, Quantity = 1 };
		var slotB = new InventoryItemSlot { Item = ringB, Quantity = 1 };
		inventory.Items.Add(slotA);
		inventory.Items.Add(slotB);

		inventory.Equip(slotA);
		inventory.Equip(slotB);

		AssertObject(inventory.EquippedItems[EquipmentSlot.LeftRing]).IsEqual(slotA);
		AssertObject(inventory.EquippedItems[EquipmentSlot.RightRing]).IsEqual(slotB);
	}

	/// <summary>Every class starts wearing body armor and a helmet.</summary>
	[TestCase]
	public void EveryClassStartsInBodyArmorAndAHelmet()
	{
		foreach (CharacterClass characterClass in CharacterCatalog.LoadDefault().Classes)
		{
			Inventory inventory = characterClass.CreateStartingInventory(20);
			foreach (var slot in inventory.Items.ToArray())
				if (slot.Item is EquipableItem) inventory.Equip(slot);

			AssertObject(inventory.EquippedItems[EquipmentSlot.Head]?.Item).OverrideFailureMessage(characterClass.Id).IsInstanceOf<Armor>();
			AssertObject(inventory.EquippedItems[EquipmentSlot.Chest]?.Item).OverrideFailureMessage(characterClass.Id).IsInstanceOf<Armor>();
		}
	}

	/// <summary>Worn body armor adds its armor to the hero's, and a heavy piece slows them.</summary>
	[TestCase]
	public async Task WornBodyArmorArmorsAndSlowsTheWearer()
	{
		var player = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>());
		await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
		float armor = player.Stats.Armor, speed = player.Stats.Speed;
		var plate = (Armor)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/armor/body/iron_breastplate.tres"));
		AssertBool(player.Inventory.TryAdd(plate).Success).IsTrue();

		AssertBool(player.InventoryController.TryEquip(player.Inventory.Items.Last()).Success).IsTrue();

		AssertObject(player.Inventory.EquippedItems[EquipmentSlot.Chest]?.Item).IsSame(plate);
		AssertFloat(player.Stats.Armor).IsEqual(armor + plate.ScaledArmorPoints);
		AssertFloat(player.HurtBoxComponent.Armor).IsEqual(player.Stats.Armor);
		AssertFloat(player.Stats.Speed).IsEqualApprox(speed * plate.SpeedModifier, 0.001f);
	}

	[TestCase]
	public void WeaponAutoEquipsToMainHandEvenWithEmptyOffhand()
	{
		var inventory = new Inventory();
		var dagger = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/dagger_common.tres");
		var slot = new InventoryItemSlot { Item = dagger, Quantity = 1 };
		inventory.Items.Add(slot);

		inventory.Equip(slot);

		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsEqual(slot);
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
	}

	[TestCase]
	public void TwoHandedWeaponDisplacesOffhand()
	{
		var inventory = new Inventory();
		var dagger = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/dagger_common.tres");
		var twoHanded = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/greatsword/claymore_iron.tres");
		var slotDagger = new InventoryItemSlot { Item = dagger, Quantity = 1 };
		var slotTwoHanded = new InventoryItemSlot { Item = twoHanded, Quantity = 1 };
		inventory.Items.Add(slotDagger);
		inventory.Items.Add(slotTwoHanded);

		inventory.Equip(slotDagger, EquipmentSlot.ShieldHand);
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsEqual(slotDagger);

		// Equipping 2H weapon must displace the offhand item
		inventory.Equip(slotTwoHanded, EquipmentSlot.WeaponHand);
		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsEqual(slotTwoHanded);
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
	}

	[TestCase]
	public async Task OpeningEquipmentPanelShowsTwoHandedWeaponInBothHands()
	{
		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/ui/equipment_panel.tscn");
		await runner.SimulateFrames(1, 20);
		var panel = (EquipmentPanel)runner.Scene();
		var inventory = new Inventory();
		var weapon = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/crossbow_common.tres"));
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

	[TestCase]
	public void AnyOneHandedWeaponCanExplicitlyEquipToOffhand()
	{
		var inventory = new Inventory();
		var swordA = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/sword_common.tres");
		var swordB = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/axe_common.tres");
		var slotA = new InventoryItemSlot { Item = swordA, Quantity = 1 };
		var slotB = new InventoryItemSlot { Item = swordB, Quantity = 1 };
		inventory.Items.Add(slotA);
		inventory.Items.Add(slotB);

		inventory.Equip(slotA, EquipmentSlot.WeaponHand);
		inventory.Equip(slotB, EquipmentSlot.ShieldHand);

		AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]).IsEqual(slotA);
		AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsEqual(slotB);
		AssertObject(inventory.EquippedWeapon).IsEqual(swordA);
		AssertObject(inventory.EquippedOffhandWeapon).IsEqual(swordB);
	}

	[TestCase]
	public async Task PortableLightInOffHandGrantsMakeshiftGuard()
	{
		using var harnessRunner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(harnessRunner, 3);
		var player = harness.Player;

		var torch = (EquipableItem)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/tools/lamp/tool_torch_wooden.tres"));
		AssertObject(torch.Guard).IsNotNull();
		AssertBool(player.Inventory.TryAdd(torch).Success).IsTrue();
		player.Inventory.Equip(player.Inventory.Items.First(entry => entry.Item == torch), EquipmentSlot.ShieldHand);

		// Equipping alone refreshes the guard: the held light's own guard replaces the bare-handed one.
		AssertObject(player.GuardComponent.EffectiveDefinition).IsSame(torch.Guard);
	}

	[TestCase]
	public async Task ContextMenuConfiguresHandOptionsAccurately()
	{
		using var runner = TestFactories.LoadScene("res://scenes/ui/inventory_item_context_menu.tscn");
		await runner.SimulateFrames(1, 20);
		var menu = (InventoryItemContextMenu)runner.Scene();
		var inventory = new Inventory();

		var sword = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/sword_common.tres");
		var twoHanded = TestFactories.Load<EquipableItem>("res://scenes/items/weapons/greatsword/claymore_iron.tres");
		var shield = TestFactories.Load<EquipableItem>("res://scenes/items/armor/shield_common.tres");
		var torch = TestFactories.Load<EquipableItem>("res://scenes/items/tools/lamp/tool_torch_wooden.tres");

		var slotSword = new InventoryItemSlot { Item = sword, Quantity = 1 };
		var slot2H = new InventoryItemSlot { Item = twoHanded, Quantity = 1 };
		var slotShield = new InventoryItemSlot { Item = shield, Quantity = 1 };
		var slotTorch = new InventoryItemSlot { Item = torch, Quantity = 1 };

		inventory.Items.Add(slotSword);
		inventory.Items.Add(slot2H);
		inventory.Items.Add(slotShield);
		inventory.Items.Add(slotTorch);

		var equipMain = menu.GetNode<Button>("%EquipButton");
		var equipOff = menu.GetNode<Button>("%EquipOtherHandButton");
		var unequip = menu.GetNode<Button>("%UnequipButton");

		// 1H Sword: both hands enabled
		menu.Initialize(inventory, slotSword);
		AssertBool(equipMain.Visible).IsTrue();
		AssertBool(equipMain.Disabled).IsFalse();
		AssertBool(equipOff.Visible).IsTrue();
		AssertBool(equipOff.Disabled).IsFalse();
		AssertBool(unequip.Visible).IsFalse();

		// 2H Greatsword: main hand enabled, off hand disabled
		menu.Initialize(inventory, slot2H);
		AssertBool(equipMain.Visible).IsTrue();
		AssertBool(equipMain.Disabled).IsFalse();
		AssertBool(equipOff.Visible).IsTrue();
		AssertBool(equipOff.Disabled).IsTrue();

		// Shield: main hand disabled, off hand enabled
		menu.Initialize(inventory, slotShield);
		AssertBool(equipMain.Visible).IsTrue();
		AssertBool(equipMain.Disabled).IsTrue();
		AssertBool(equipOff.Visible).IsTrue();
		AssertBool(equipOff.Disabled).IsFalse();

		// Torch: main hand disabled, off hand enabled
		menu.Initialize(inventory, slotTorch);
		AssertBool(equipMain.Visible).IsTrue();
		AssertBool(equipMain.Disabled).IsTrue();
		AssertBool(equipOff.Visible).IsTrue();
		AssertBool(equipOff.Disabled).IsFalse();

		// When sword is equipped in main hand: main hand disabled, off hand enabled, unequip visible
		inventory.Equip(slotSword, EquipmentSlot.WeaponHand);
		menu.Initialize(inventory, slotSword);
		AssertBool(equipMain.Disabled).IsTrue();
		AssertBool(equipOff.Disabled).IsFalse();
		AssertBool(unequip.Visible).IsTrue();
	}
}
