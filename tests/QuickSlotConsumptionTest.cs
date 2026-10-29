namespace RogueGauntlet.Tests;

using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Drinking a picked-up potion through its quick slot consumes it through the real player controllers, and the slot
/// stays bound as a depleted stack whose HUD notification shows 0 (it used to announce the previous count).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class QuickSlotConsumptionTest
{
	[TestCase]
	public async Task DrinkingTheLastPotionFromAQuickSlotLeavesItDepleted()
	{
		ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(10);
		Player player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();
		var potion = ItemFactory.Create(GD.Load<Item>("res://scenes/items/potions/potion_healing_small.tres"));
		AssertBool(player.InventoryController.PickupItem(potion)).IsTrue();
		PlayerQuickItemController quick = player.QuickItemController;
		int slot = Enumerable.Range(0, PlayerQuickItemController.SlotCount)
			.First(i => quick.GetBinding(i).DefinitionPath == potion.DefinitionPath);
		int before = quick.GetSlotCount(slot);
		AssertInt(before).IsGreater(0);

		int consumed = 0;
		int shownQuantity = -1;
		player.Inventory.ItemConsumed += _ => consumed++;
		// What the HUD's action container reads when it is notified.
		void OnSlotChanged(int index, ActionSlot action) { if (index == slot) { shownQuantity = action.Quantity; } }
		SignalBus.Instance.PlayerActionSlotChanged += OnSlotChanged;
		for (int drink = 0; drink < before; drink++)
		{
			ItemOperationResult result = quick.TryUseSlot(slot);
			AssertBool(result.Success).OverrideFailureMessage($"drink {drink + 1}: {result.Failure}").IsTrue();
			for (int i = 0; i < 60 && consumed <= drink; i++) { await runner.SimulateFrames(5, 20); }
			for (int i = 0; i < 60 && quick.IsVarietyOnCooldown(potion.DefinitionPath); i++) { await runner.SimulateFrames(5, 20); }
		}

		AssertInt(consumed).IsEqual(before);
		AssertInt(quick.GetSlotCount(slot)).IsEqual(0);
		AssertBool(quick.IsSlotDepleted(slot)).IsTrue();
		AssertInt(player.ActionManager.GetActionSlot(slot).Quantity).IsEqual(0);
		AssertInt(shownQuantity).IsEqual(0);
		SignalBus.Instance.PlayerActionSlotChanged -= OnSlotChanged;
	}
}
