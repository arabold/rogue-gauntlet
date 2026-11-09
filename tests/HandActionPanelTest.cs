namespace RogueGauntlet.Tests;

using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class HandActionPanelTest
{
	[TestCase]
	public async Task HandActionPanelLoadsAndWiresComponents()
	{
		using var runner = TestFactories.LoadScene("res://scenes/ui/hand_action_panel.tscn");
		await runner.SimulateFrames(1);
		var panel = (HandActionPanel)runner.Scene();

		AssertObject(panel.Preview).IsNotNull();
		AssertObject(panel.FirstActionKey).IsNotNull();
		AssertObject(panel.SecondActionKey).IsNotNull();
		AssertObject(panel.FirstActionCooldownBar).IsNotNull();
		AssertObject(panel.SecondActionCooldownBar).IsNotNull();
	}

	[TestCase]
	public async Task MainHandShowsEquippedWeaponAndEnablesKeys()
	{
		using var harnessRunner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(harnessRunner, 3);
		var player = harness.Player;

		using var runner = TestFactories.LoadScene("res://scenes/ui/hand_action_panel.tscn");
		await runner.SimulateFrames(1);
		var panel = (HandActionPanel)runner.Scene();
		panel.BindPlayer(player);
		panel.Hand = EquipmentSlot.WeaponHand;

		var sword = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword_common.tres"));
		player.Inventory.TryAdd(sword);
		player.Inventory.Equip(player.Inventory.Items[^1], EquipmentSlot.WeaponHand);

		panel.Refresh();

		AssertBool(panel.FirstActionKey.IsDisabled).IsFalse();
		AssertObject(panel.Preview.Scene).IsNotNull();
	}

	[TestCase]
	public async Task OffHandDimsAndDisablesWhenMeleeDaggerEquipped()
	{
		using var harnessRunner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(harnessRunner, 3);
		var player = harness.Player;

		using var runner = TestFactories.LoadScene("res://scenes/ui/hand_action_panel.tscn");
		await runner.SimulateFrames(1);
		var panel = (HandActionPanel)runner.Scene();
		panel.BindPlayer(player);
		panel.Hand = EquipmentSlot.ShieldHand;

		var dagger = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/dagger_common.tres"));
		player.Inventory.TryAdd(dagger);
		player.Inventory.Equip(player.Inventory.Items[^1], EquipmentSlot.ShieldHand);
		player.InventoryController.RefreshEffectiveGuard();

		panel.Refresh();

		// Melee dagger in off hand has no L action: L key is disabled and preview is dimmed
		AssertBool(panel.FirstActionKey.IsDisabled).IsTrue();
		AssertObject(panel.Preview.Scene).IsNotNull();
		AssertObject(panel.ViewportContainer.Modulate).IsEqual(new Color(0.4f, 0.4f, 0.4f, 0.6f));
	}

	[TestCase]
	public async Task OffHandEnablesGuardWhenShieldEquipped()
	{
		using var harnessRunner = TestFactories.LoadCombatHarness();
		var harness = await TestFactories.SettleCombatHarness(harnessRunner, 3);
		var player = harness.Player;

		using var runner = TestFactories.LoadScene("res://scenes/ui/hand_action_panel.tscn");
		await runner.SimulateFrames(1);
		var panel = (HandActionPanel)runner.Scene();
		panel.BindPlayer(player);
		panel.Hand = EquipmentSlot.ShieldHand;

		var shield = (EquipableItem)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/armor/shield_common.tres"));
		player.Inventory.TryAdd(shield);
		player.Inventory.Equip(player.Inventory.Items[^1], EquipmentSlot.ShieldHand);
		player.InventoryController.RefreshEffectiveGuard();

		panel.Refresh();

		AssertBool(panel.FirstActionKey.IsDisabled).IsFalse();
		AssertObject(panel.Preview.Scene).IsNotNull();
		AssertObject(panel.ViewportContainer.Modulate).IsEqual(Colors.White);
	}
}
