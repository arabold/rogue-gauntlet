namespace RogueGauntlet.Tests;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Verifies the RunMenu tab order (<c>Gear</c>, <c>Pack</c>, <c>Notes</c>, <c>System</c>), live and
/// historical log playback in <see cref="NotesTab"/>, and fixed-size auto-scrolling layout for long item
/// titles and multi-line item details in <see cref="PackTab"/> and <see cref="GearTab"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RunMenuNotesAndLayoutTest
{
	[TestCase]
	public async Task NotesTabSitsBetweenPackAndSystemAndDisplaysRunLogs()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		GameLog.Player("You reached [b]floor level 2[/b]", LogSeverity.Info);

		using ISceneRunner runner = TestFactories.LoadCombatHarness();
		CombatHarness harness = await TestFactories.SettleCombatHarness(runner, 3);
		RunMenu menu = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>());
		try
		{
			var tabButtons = menu.GetNode<HBoxContainer>("Header/Tabs").GetChildren().OfType<Button>().ToArray();
			AssertArray(tabButtons.Select(b => b.Text).ToArray()).ContainsExactly("Gear", "Pack", "Notes", "System");
			AssertInt((int)RunMenuPage.Notes).IsEqual(2);
			AssertInt((int)RunMenuPage.System).IsEqual(3);

			menu.Open(harness.Player, RunMenuPage.Notes);
			var notes = (NotesTab)menu.NotesTab;
			await runner.SimulateFrames(2, 20);

			AssertInt((int)menu.Page.Value).IsEqual((int)RunMenuPage.Notes);
			AssertBool(notes.Entries.GetChildren().OfType<RichTextLabel>().Any(r => r.GetParsedText().Contains("floor level 2"))).IsTrue();

			GameLog.Player("Ulch - that [b]Crisp Red Apple[/b] was tainted!", LogSeverity.Warning);
			await runner.SimulateFrames(1, 20);
			AssertBool(notes.Entries.GetChildren().OfType<RichTextLabel>().Any(r => r.GetParsedText().Contains("tainted"))).IsTrue();
		}
		finally
		{
			menu.Close();
			GameLog.ResetToDefaults();
		}
	}

	[TestCase]
	public async Task LongItemTitleAndDetailsDoNotResizeColumnsOrPushPreviewAndScrollOverTime()
	{
		using ISceneRunner runner = TestFactories.LoadCombatHarness();
		CombatHarness harness = await TestFactories.SettleCombatHarness(runner, 3);
		RunMenu menu = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>());
		try
		{
			var shortSword = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword_common.tres"));

			var longStaff = (Weapon)ItemFactory.Create(TestFactories.Load<Item>("res://scenes/items/weapons/sword_common.tres"));
			longStaff.Quality = ItemQuality.Rare;
			longStaff.Affixes =
			[
				new RolledAffix { Kind = AffixKind.Prefix, NameFragment = "Sharp Archmage Astral" },
				new RolledAffix { Kind = AffixKind.Suffix, NameFragment = "of Meteors and Eternal Destruction" },
			];
			longStaff.AttributesKnown = true;
			longStaff.CurseStatusKnown = true;
			longStaff.CursePenalty =
			[
				new StatModifier { Stat = StatType.Strength, Value = -3 },
				new StatModifier { Stat = StatType.Dexterity, Value = -2 },
				new StatModifier { Stat = StatType.Vitality, Value = -4 },
				new StatModifier { Stat = StatType.Intelligence, Value = -5 },
				new StatModifier { Stat = StatType.Armor, Value = -6 },
				new StatModifier { Stat = StatType.Speed, Op = ModifierOp.Percent, Value = -0.15f },
				new StatModifier { Stat = StatType.MinDamage, Value = -4 },
				new StatModifier { Stat = StatType.MaxDamage, Value = -8 },
				new StatModifier { Stat = StatType.CritChance, Value = -0.05f },
			];

			AssertBool(harness.Player.Inventory.TryAdd(shortSword).Success).IsTrue();
			AssertBool(harness.Player.Inventory.TryAdd(longStaff).Success).IsTrue();

			menu.Open(harness.Player, RunMenuPage.Pack);
			var pack = (PackTab)menu.PackTab;
			var itemsColumn = pack.GetNode<Control>("Columns/Items");
			var infoBox = pack.GetNode<Control>("Columns/Selected/Info");
			var nameRow = pack.GetNode<Control>("Columns/Selected/Info/NameClip/Name");
			var nameClip = pack.GetNode<AutoScrollViewport>("Columns/Selected/Info/NameClip");
			var detailsClip = pack.GetNode<AutoScrollViewport>("Columns/Selected/Info/DetailsClip");

			ItemSlotPanel SlotFor(Item item) => pack.Grid.GetChildren().OfType<ItemSlotPanel>().First(p => p.Slot?.Item == item);

			SlotFor(shortSword).FocusSlot();
			await runner.SimulateFrames(2, 20);
			float baselineItemsWidth = itemsColumn.Size.X;
			float baselineItemsPosX = itemsColumn.Position.X;
			float baselineInfoHeight = infoBox.Size.Y;

			SlotFor(longStaff).FocusSlot();
			await runner.SimulateFrames(2, 20);
			AssertFloat(itemsColumn.Size.X).IsEqualApprox(baselineItemsWidth, 1f);
			AssertFloat(itemsColumn.Position.X).IsEqualApprox(baselineItemsPosX, 1f);
			AssertFloat(infoBox.Size.Y).IsEqualApprox(baselineInfoHeight, 1f);
			AssertBool(nameClip.ClipContents).IsTrue();
			AssertBool(detailsClip.ClipContents).IsTrue();

			// Advance past the edge pause so horizontal title scroll and vertical details scroll both move cleanly.
			nameClip._Process(2.0);
			detailsClip._Process(2.0);
			AssertFloat(nameRow.Position.X).IsLess(0f);
			AssertFloat(pack.Details.Position.Y).IsLess(0f);

			// Verify GearTab keeps fixed Info height and clips long names too.
			menu.Open(harness.Player, RunMenuPage.Gear);
			var gear = (GearTab)menu.GearTab;
			var gearInfo = gear.GetNode<Control>("Columns/Center/Info");
			var gearNameClip = gear.GetNode<AutoScrollViewport>("Columns/Center/Info/NameClip");
			var gearDetailsClip = gear.GetNode<AutoScrollViewport>("Columns/Center/Info/DetailsClip");
			var gearLongSlot = gear.GearGrid.GetChildren().OfType<ItemSlotPanel>().First(p => p.Slot?.Item == longStaff);
			gearLongSlot.FocusSlot();
			await runner.SimulateFrames(2, 20);
			AssertFloat(gearInfo.Size.Y).IsEqualApprox(328f, 1f);
			gearNameClip._Process(2.0);
			gearDetailsClip._Process(2.0);
			AssertFloat(gear.NameLabel.Position.X).IsLess(0f);
			AssertFloat(gear.Details.Position.Y).IsLess(0f);
		}
		finally
		{
			menu.Close();
		}
	}

	[TestCase]
	public async Task RunNotesPersistAcrossSaveAndReload()
	{
		using var saves = new ScratchSaves();
		GameSession.Instance.BeginRun(1, "barbarian", 4242);

		using ISceneRunner runner = TestFactories.LoadScene("res://scenes/main/main.tscn");
		await runner.SimulateFrames(4, 20);

		GameLog.Player("Ulch - that [b]Crisp Red Apple[/b] was tainted!", LogSeverity.Warning);
		GameLog.Player("You feel very sick and nauseous.", LogSeverity.Warning);
		GameSession.Instance.Notification((int)Node.NotificationWMCloseRequest);

		SaveGame saved = SaveService.Load(1);
		AssertObject(saved).IsNotNull();
		AssertBool(saved.Notes.Any(n => n.Message == "You reached [b]floor level 1[/b]")).IsTrue();
		AssertBool(saved.Notes.Any(n => n.Message == "Ulch - that [b]Crisp Red Apple[/b] was tainted!"
			&& n.Severity == (int)LogSeverity.Warning)).IsTrue();
		AssertBool(saved.Notes.Any(n => n.Message == "You feel very sick and nauseous.")).IsTrue();

		SaveGame roundTripped = SaveService.DeserializeCurrentVersion(JsonSerializer.Serialize(saved));
		GameLog.ClearPlayerHistory();
		GameLog.RestorePlayerHistory(roundTripped.Notes.Select(n =>
			new LogEntry(LogChannel.Player, n.Message, (LogSeverity)n.Severity, LogCategory.General)));

		var player = runner.Scene().GetTree().GetNodesInGroup("player").OfType<Player>().First();
		RunMenu menu = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>());
		try
		{
			menu.Open(player, RunMenuPage.Notes);
			var notes = (NotesTab)menu.NotesTab;
			await runner.SimulateFrames(2, 20);
			string[] shown = notes.Entries.GetChildren().OfType<RichTextLabel>().Select(r => r.GetParsedText()).ToArray();
			AssertBool(shown.Any(t => t.Contains("floor level 1"))).IsTrue();
			AssertBool(shown.Any(t => t.Contains("tainted"))).IsTrue();
			AssertBool(shown.Any(t => t.Contains("very sick and nauseous"))).IsTrue();
		}
		finally
		{
			menu.Close();
		}
	}

	[TestCase]
	public async Task NotesTabOverflowAutomaticallyShowsNewestAndAllowsScrolling()
	{
		GameLog.ResetToDefaults();
		GameLog.IsConsoleSinkEnabled = false;
		for (int i = 1; i <= 60; i++)
		{
			GameLog.Player($"Log entry #{i}: explored a long winding stone passage.", LogSeverity.Info);
		}

		using ISceneRunner runner = TestFactories.LoadCombatHarness();
		CombatHarness harness = await TestFactories.SettleCombatHarness(runner, 3);
		RunMenu menu = TestFactories.AddToSceneTree(
			TestFactories.Load<PackedScene>("res://scenes/ui/menu/run_menu.tscn").Instantiate<RunMenu>());
		try
		{
			menu.Open(harness.Player, RunMenuPage.Notes);
			var notes = (NotesTab)menu.NotesTab;
			await runner.SimulateFrames(3, 20);

			VScrollBar bar = notes.Scroll.GetVScrollBar();
			int expectedMax = Mathf.Max(0, Mathf.CeilToInt(bar.MaxValue - bar.Page));
			AssertInt(expectedMax).IsGreater(0);
			AssertInt(notes.Scroll.ScrollVertical).IsBetween(expectedMax - 4, expectedMax);

			// Simulate pressing Page Up to scroll up and confirm position moves cleanly away from bottom.
			notes._UnhandledInput(new InputEventAction { Action = "ui_page_up", Pressed = true });
			await runner.SimulateFrames(1, 20);
			int scrolledUp = notes.Scroll.ScrollVertical;
			AssertInt(scrolledUp).IsLess(expectedMax);

			// Emitting a new message brings the newest entry back into view at the bottom.
			GameLog.Player("You spotted a secret doorway!", LogSeverity.Discovery);
			await runner.SimulateFrames(3, 20);
			int updatedMax = Mathf.Max(0, Mathf.CeilToInt(bar.MaxValue - bar.Page));
			AssertInt(notes.Scroll.ScrollVertical).IsBetween(updatedMax - 4, updatedMax);
		}
		finally
		{
			menu.Close();
			GameLog.ResetToDefaults();
		}
	}

	private sealed class ScratchSaves : IDisposable
	{
		private readonly string _previous = SaveService.SaveDirectory;
		private readonly string _directory = $"user://test-notes-saves-{Guid.NewGuid():N}";

		public ScratchSaves() => SaveService.SaveDirectory = _directory;

		public void Dispose()
		{
			GameSession.Instance?.ClearSession();
			GameLog.ResetToDefaults();
			((SceneTree)Engine.GetMainLoop()).Paused = false;
			SaveService.SaveDirectory = _previous;
			string path = ProjectSettings.GlobalizePath(_directory);
			if (Directory.Exists(path)) Directory.Delete(path, true);
		}
	}
}