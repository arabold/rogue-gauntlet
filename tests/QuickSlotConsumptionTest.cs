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
		using ISceneRunner runner = TestFactories.LoadCombatHarness();
		Player player = (await TestFactories.SettleCombatHarness(runner)).Player;
		var potion = ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/potions/potion_healing_small.tres"));
		// Only the last potion matters: drop any starting stack so the pickup is the only one carried.
		foreach (InventoryItemSlot carried in player.Inventory.Items.Where(entry => entry.Item.DefinitionPath == potion.DefinitionPath).ToList())
		{
			player.Inventory.RemoveItem(carried, carried.Quantity);
		}
		AssertBool(player.InventoryController.PickupItem(potion)).IsTrue();
		PlayerQuickItemController quick = player.QuickItemController;
		int slot = Enumerable.Range(0, PlayerQuickItemController.SlotCount)
			.First(i => quick.GetBinding(i).DefinitionPath == potion.DefinitionPath);
		AssertInt(quick.GetSlotCount(slot)).IsEqual(1);

		int consumed = 0;
		int shownQuantity = -1;
		player.Inventory.ItemConsumed += _ => consumed++;
		// What the HUD's action container reads when it is notified.
		void OnSlotChanged(int index, ActionSlot action) { if (index == slot) { shownQuantity = action.Quantity; } }
		SignalBus.Instance.PlayerActionSlotChanged += OnSlotChanged;
		try
		{
			ItemOperationResult result = quick.TryUseSlot(slot);
			AssertBool(result.Success).OverrideFailureMessage($"drink: {result.Failure}").IsTrue();
			for (int i = 0; i < 60 && consumed == 0; i++) { await runner.SimulateFrames(5, 20); }
		}
		finally
		{
			SignalBus.Instance.PlayerActionSlotChanged -= OnSlotChanged;
		}

		AssertInt(consumed).IsEqual(1);
		AssertInt(quick.GetSlotCount(slot)).IsEqual(0);
		AssertBool(quick.IsSlotDepleted(slot)).IsTrue();
		AssertInt(player.ActionManager.GetActionSlot(slot).Quantity).IsEqual(0);
		AssertInt(shownQuantity).IsEqual(0);
	}
}
