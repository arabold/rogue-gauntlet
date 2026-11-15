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

	/// <summary>Worn body armor adds its armor to the hero's, and a heavy piece slows them.</summary>
	[TestCase]
	public async Task WornBodyArmorArmorsAndSlowsTheWearer()
	{
		var player = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>());
		await player.ToSignal(player.GetTree(), SceneTree.SignalName.PhysicsFrame);
		float armor = player.Stats.Armor, speed = player.Stats.Speed;
		var plate = (Armor)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/armor/body/steel_plate.tres"));
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
	public async Task GearTabShowsATwoHandedWeaponInBothHands()
	{
		using var runner = TestFactories.LoadCombatHarness();
		(RunMenu menu, GearTab gear, Player player) = await OpenGear(runner);
		try
		{
			Item weapon = Carry(player, "res://scenes/items/weapons/crossbow_common.tres");
			player.InventoryController.TryEquip(Entry(player, weapon));

			AssertObject(gear.MainHandSlot.Slot.Item).IsEqual(weapon);
			AssertObject(gear.OffHandSlot.Slot.Item).IsEqual(weapon);
			// The mirrored slot must not create a second equipment owner.
			AssertObject(player.Inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();

			// Turning away and back keeps it; taking the weapon off empties both hands.
			menu.Open(player, RunMenuPage.Pack);
			menu.Open(player, RunMenuPage.Gear);
			AssertObject(gear.OffHandSlot.Slot.Item).IsEqual(weapon);
			gear.OffHandSlot.FocusSlot();
			gear.Invoke("ui_accept");
			AssertObject(gear.MainHandSlot.Slot).IsNull();
			AssertObject(gear.OffHandSlot.Slot).IsNull();
		}
		finally
		{
			menu.Close();
		}
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
	public async Task GearTabOffersEachHandOnlyToWhatFitsIt()
	{
		using var runner = TestFactories.LoadCombatHarness();
		(RunMenu menu, GearTab gear, Player player) = await OpenGear(runner);
		try
		{
			Inventory inventory = player.Inventory;
			foreach (InventoryItemSlot worn in inventory.Items.Where(inventory.IsEquipped).ToArray())
			{
				inventory.Unequip(worn);
			}
			Item sword = Carry(player, "res://scenes/items/weapons/sword_common.tres");
			Item twoHanded = Carry(player, "res://scenes/items/weapons/greatsword/claymore_iron.tres");
			Item shield = Carry(player, "res://scenes/items/armor/shield/shield_common.tres");
			Item torch = Carry(player, "res://scenes/items/tools/lamp/tool_torch_wooden.tres");

			// What the tab offers for an item once it is selected in the gear list.
			string[] Offers(Item item)
			{
				gear.GearGrid.GetChildren().OfType<ItemSlotPanel>().First(panel => panel.Slot.Item == item).FocusSlot();
				return [.. gear.Prompts.Select(prompt => prompt.Label)];
			}

			// A one-handed sword goes in either hand; a two-handed one only in the main hand.
			AssertArray(Offers(sword)).ContainsExactly("Main Hand", "Off Hand", "Drop");
			AssertArray(Offers(twoHanded)).ContainsExactly("Equip", "Drop");

			// A shield and a torch have one hand to go to, so choosing them puts them there.
			AssertArray(Offers(shield)).ContainsExactly("Equip", "Drop");
			gear.Invoke("ui_accept");
			AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]?.Item).IsEqual(shield);
			AssertArray(Offers(torch)).ContainsExactly("Equip", "Drop");
			gear.Invoke("ui_accept");
			AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]?.Item).IsEqual(torch);

			// Worn, the sword can come off or change to the other hand, and cannot be dropped.
			Offers(sword);
			gear.Invoke("ui_accept");
			AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]?.Item).IsEqual(sword);
			AssertArray(Offers(sword)).ContainsExactly("Unequip", "Off Hand");
			gear.Invoke("menu_alternate");
			AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]?.Item).IsEqual(sword);
			AssertArray(Offers(sword)).ContainsExactly("Unequip", "Main Hand");
			gear.Invoke("menu_alternate");
			AssertObject(inventory.EquippedItems[EquipmentSlot.WeaponHand]?.Item).IsEqual(sword);
			AssertObject(inventory.EquippedItems[EquipmentSlot.ShieldHand]).IsNull();
		}
		finally
		{
			menu.Close();
		}
	}

	/// <summary>Opens the run menu's Gear tab for the combat harness's hero. The menu pauses the game: close it again.</summary>
	private static async Task<(RunMenu Menu, GearTab Gear, Player Player)> OpenGear(ISceneRunner runner)
	{
		CombatHarness harness = await TestFactories.SettleCombatHarness(runner, 3);
		RunMenu menu = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>());
		menu.Open(harness.Player, RunMenuPage.Gear);
		return (menu, (GearTab)menu.GearTab, harness.Player);
	}

	private static Item Carry(Player player, string path)
	{
		Item item = ItemFactory.Create(TestFactories.Load<Item>(path));
		AssertBool(player.Inventory.TryAdd(item).Success).IsTrue();
		return item;
	}

	private static InventoryItemSlot Entry(Player player, Item item) => player.Inventory.Items.First(entry => entry.Item == item);
}
