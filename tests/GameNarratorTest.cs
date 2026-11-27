namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class GameNarratorTest
{
	private class NarratorTestSink : ILogSink
	{
		public readonly List<LogEntry> Entries = new();

		public void Log(in LogEntry entry)
		{
			Entries.Add(entry);
		}
	}

	[TestCase]
	public void ItemConsumedWithPriorRevealEmitsDisguisedActionAndReveal()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new NarratorTestSink();
		GameLog.AddSink(sink);

		var narrator = TestFactories.AddToSceneTree(new GameNarrator());
		var potion = new TestFactories.TestConsumableItem("potion.healing", "Small Potion of Healing", "drink_potion");

		// Simulate reveal then consumption
		SignalBus.EmitItemIdentityRevealed("potion.healing", "Small Murky Potion", "Small Potion of Healing", nameof(IdentificationSource.Use));
		SignalBus.EmitItemConsumed(null, potion);

		AssertInt(sink.Entries.Count).IsEqual(2);
		AssertString(sink.Entries[0].Message).IsEqual("Drank [b]Small Murky Potion[/b]");
		AssertInt((int)sink.Entries[0].Severity).IsEqual((int)LogSeverity.Info);
		AssertString(sink.Entries[1].Message).IsEqual("[b]Small Murky Potion[/b] is [b]Small Potion of Healing[/b]");
		AssertInt((int)sink.Entries[1].Severity).IsEqual((int)LogSeverity.Discovery);

		narrator.QueueFree();
		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void ItemConsumedWithoutRevealUsesResolvedName()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new NarratorTestSink();
		GameLog.AddSink(sink);

		var narrator = TestFactories.AddToSceneTree(new GameNarrator());
		var scroll = new TestFactories.TestScroll("Scroll of Teleportation");

		SignalBus.EmitItemConsumed(null, scroll);

		AssertInt(sink.Entries.Count).IsEqual(1);
		AssertString(sink.Entries[0].Message).IsEqual("Read [b]Scroll of Teleportation[/b]");

		narrator.QueueFree();
		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void StandaloneRevealFlushesOnProcess()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new NarratorTestSink();
		GameLog.AddSink(sink);

		var narrator = TestFactories.AddToSceneTree(new GameNarrator());

		SignalBus.EmitItemIdentityRevealed("scroll.identify", "Scroll labeled ZELGO MER", "Scroll of Identify", nameof(IdentificationSource.Use));
		narrator.FlushPendingReveals();

		AssertInt(sink.Entries.Count).IsEqual(1);
		AssertString(sink.Entries[0].Message).IsEqual("[b]Scroll labeled ZELGO MER[/b] is [b]Scroll of Identify[/b]");
		AssertInt((int)sink.Entries[0].Severity).IsEqual((int)LogSeverity.Discovery);

		narrator.QueueFree();
		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void EquipmentActionsUseFittingVerbs()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new NarratorTestSink();
		GameLog.AddSink(sink);

		var narrator = TestFactories.AddToSceneTree(new GameNarrator());
		var sword = new TestFactories.TestEquipableItem("Iron Broadsword", ValidSlots.WeaponHand);
		var ring = new TestFactories.TestEquipableItem("Gold Ring", ValidSlots.LeftRing);
		var armor = new TestFactories.TestEquipableItem("Plate Armor", ValidSlots.Chest);

		SignalBus.EmitItemEquipped(null, sword);
		SignalBus.EmitItemEquipped(null, ring);
		SignalBus.EmitItemEquipped(null, armor);
		SignalBus.EmitItemUnequipped(null, sword);

		AssertInt(sink.Entries.Count).IsEqual(4);
		AssertString(sink.Entries[0].Message).IsEqual("Wielded [b]Iron Broadsword[/b]");
		AssertInt((int)sink.Entries[0].Severity).IsEqual((int)LogSeverity.Equipment);
		AssertString(sink.Entries[1].Message).IsEqual("Put on [b]Gold Ring[/b]");
		AssertInt((int)sink.Entries[1].Severity).IsEqual((int)LogSeverity.Equipment);
		AssertString(sink.Entries[2].Message).IsEqual("Equipped [b]Plate Armor[/b]");
		AssertInt((int)sink.Entries[2].Severity).IsEqual((int)LogSeverity.Equipment);
		AssertString(sink.Entries[3].Message).IsEqual("Unequipped [b]Iron Broadsword[/b]");
		AssertInt((int)sink.Entries[3].Severity).IsEqual((int)LogSeverity.Equipment);

		narrator.QueueFree();
		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void DropPickupXpAndDeathEmitAppropriateMessages()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new NarratorTestSink();
		GameLog.AddSink(sink);

		var narrator = TestFactories.AddToSceneTree(new GameNarrator());
		var gold = new TestFactories.TestGold("Gold");
		var arrows = new TestFactories.TestItem("Arrow");

		SignalBus.EmitItemPickedUp(null, gold, 50);
		SignalBus.EmitItemPickedUp(null, arrows, 10);
		SignalBus.EmitItemDropped(null, arrows, 5);
		SignalBus.EmitXpRewarded(100);
		SignalBus.EmitPlayerDeath(null);

		AssertInt(sink.Entries.Count).IsEqual(5);
		AssertString(sink.Entries[0].Message).IsEqual("Picked up [b]50 Gold[/b]");
		AssertInt((int)sink.Entries[0].Severity).IsEqual((int)LogSeverity.Gold);
		AssertString(sink.Entries[1].Message).IsEqual("Picked up [b]10 Arrow[/b]");
		AssertInt((int)sink.Entries[1].Severity).IsEqual((int)LogSeverity.ItemPickup);
		AssertString(sink.Entries[2].Message).IsEqual("Dropped [b]5 Arrow[/b]");
		AssertInt((int)sink.Entries[2].Severity).IsEqual((int)LogSeverity.ItemPickup);
		AssertString(sink.Entries[3].Message).IsEqual("Gained [b]100 XP[/b]");
		AssertInt((int)sink.Entries[3].Severity).IsEqual((int)LogSeverity.Experience);
		AssertString(sink.Entries[4].Message).IsEqual("[b]You died[/b]");
		AssertInt((int)sink.Entries[4].Severity).IsEqual((int)LogSeverity.Error);

		narrator.QueueFree();
		GameLog.ResetToDefaults();
	}

	[TestCase]
	public void LevelLoadedCursedEquipTaintedFoodAndPoisonProgressionEmitDescriptiveMessages()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		var sink = new NarratorTestSink();
		GameLog.AddSink(sink);

		var narrator = TestFactories.AddToSceneTree(new GameNarrator());
		var level = new Level { MapGenerator = new MapGenerator { DungeonDepth = 3 } };
		var player = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>());
		try
		{
			SignalBus.EmitLevelLoaded(level);
			AssertString(sink.Entries[^1].Message).IsEqual("You reached [b]floor level 3[/b]");

			var sword = (EquipableItem)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/broadsword_common.tres"));
			sword.CursePenalty = [new StatModifier { Stat = StatType.Strength, Value = -3 }];
			sword.CurseStatusKnown = false;
			SignalBus.EmitItemEquipped(player, sword);
			AssertString(sink.Entries[^1].Message).IsEqual("A dark curse saps your strength — you suddenly feel weak and exhausted.");
			AssertInt((int)sink.Entries[^1].Severity).IsEqual((int)LogSeverity.Warning);

			int beforeFood = sink.Entries.Count;
			var apple = (FoodItem)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/food/food/food_apple_red.tres"));
			apple.RestorePoison(true);
			AssertBool(player.Inventory.TryAdd(apple).Success).IsTrue();
			apple.PerformAction(player);
			SignalBus.EmitItemConsumed(player, apple);
			// Ensure tainted food warning + poison start are logged even via direct signal/BuffController calls
			AssertBool(sink.Entries.Skip(beforeFood).Any(e => e.Message == "Ulch - that [b]Crisp Red Apple[/b] was tainted!")).IsTrue();
			AssertBool(sink.Entries.Skip(beforeFood).Any(e => e.Message == "You feel very sick and nauseous.")).IsTrue();

			var venom = TestFactories.Load<PoisonBuff>("res://scenes/buffs/rat_venom.tres");
			player.ApplyBuff(venom);
			AssertString(sink.Entries[^1].Message).IsEqual("You feel worse.");

			player.ApplyBuff(venom);
			AssertString(sink.Entries[^1].Message).IsEqual("You feel much worse.");

			var healing = TestFactories.Load<Buff>("res://scenes/buffs/healing_potion_small.tres");
			player.ApplyBuff(healing);
			AssertBool(sink.Entries.Any(e => e.Message == "You are no longer ill.")).IsTrue();

			var trap = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/props/traps/pressure_plate_spikes.tscn").Instantiate<PressurePlate>());
			trap.Discover();
			AssertString(sink.Entries[^1].Message).IsEqual("You spotted a trap!");
			AssertInt((int)sink.Entries[^1].Severity).IsEqual((int)LogSeverity.Warning);

			var door = TestFactories.AddToSceneTree(TestFactories.Load<PackedScene>("res://scenes/props/door.tscn").Instantiate<Door>());
			var walls = TestFactories.AddToSceneTree(new GridMap { MeshLibrary = new MeshLibrary() });
			walls.MeshLibrary.CreateItem(0);
			walls.SetCellItem(Vector3I.Zero, 0);
			door.Secret.Initialize(walls, Vector3I.Zero, Vector3.Forward, null, 42);
			door.Secret.Discover();
			AssertString(sink.Entries[^1].Message).IsEqual("You spotted a secret doorway!");
			AssertInt((int)sink.Entries[^1].Severity).IsEqual((int)LogSeverity.Discovery);
		}
		finally
		{
			level.Free();
			narrator.QueueFree();
			GameLog.ResetToDefaults();
		}
	}
}