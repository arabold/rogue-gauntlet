namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>A hand's HUD panel shows what that hand holds and whether its action key works, following equipment changes.</summary>
[TestSuite]
[RequireGodotRuntime]
public class HandActionPanelTest
{
	private static void Equip(Player player, string path, EquipmentSlot hand)
	{
		var item = ItemFactory.Create(TestFactories.Load<Item>(path));
		AssertBool(player.Inventory.TryAdd(item).Success).IsTrue();
		player.Inventory.Equip(player.Inventory.Items.First(entry => entry.Item == item), hand);
	}

	[TestCase]
	public async Task EachHandShowsItsItemAndEnablesOnlyAvailableActions()
	{
		using var harnessRunner = TestFactories.LoadCombatHarness();
		var player = (await TestFactories.SettleCombatHarness(harnessRunner, 3)).Player;
		using var runner = TestFactories.LoadScene("res://scenes/ui/hand_action_panel.tscn");
		await runner.SimulateFrames(1, 20);
		var panel = (HandActionPanel)runner.Scene();
		panel.BindPlayer(player);

		// Main hand: the wielded sword, its attack key live.
		panel.Hand = EquipmentSlot.WeaponHand;
		Equip(player, "res://scenes/items/weapons/sword_common.tres", EquipmentSlot.WeaponHand);
		AssertBool(panel.FirstActionKey.IsDisabled).IsFalse();
		AssertObject(panel.Preview.Scene).IsNotNull();

		// Off hand with a melee dagger: shown, but it has no off-hand action, so the key is off and the preview dimmed.
		panel.Hand = EquipmentSlot.ShieldHand;
		Equip(player, "res://scenes/items/weapons/dagger_common.tres", EquipmentSlot.ShieldHand);
		AssertBool(panel.FirstActionKey.IsDisabled).IsTrue();
		AssertObject(panel.Preview.Scene).IsNotNull();
		AssertObject(panel.ViewportContainer.Modulate).IsNotEqual(Colors.White);

		// Off hand with a shield: equipping alone makes the guard key live and undims the preview.
		Equip(player, "res://scenes/items/armor/shield_common.tres", EquipmentSlot.ShieldHand);
		AssertBool(panel.FirstActionKey.IsDisabled).IsFalse();
		AssertObject(panel.Preview.Scene).IsNotNull();
		AssertObject(panel.ViewportContainer.Modulate).IsEqual(Colors.White);
	}
}