namespace RogueGauntlet.Tests;

using System.Collections.Generic;
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
		SignalBus.EmitItemIdentityRevealed("potion.healing", "Small Murky Potion", "Small Potion of Healing");
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

		SignalBus.EmitItemIdentityRevealed("scroll.identify", "Scroll labeled ZELGO MER", "Scroll of Identify");
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
}