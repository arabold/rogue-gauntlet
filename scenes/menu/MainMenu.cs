using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The front end, laid over the menu's stage. The title continues the latest run in one press or starts a new one.
/// A new run begins with its hero, picked on the stage, and takes the first free slot; only when all are taken does
/// it then ask which run to replace. Runs shows the four slots as cards: a run in progress continues, a fallen one
/// shows its record, a free one starts a run there.
/// </summary>
public partial class MainMenu : Control
{
	[Export] public MenuStage Stage { get; set; }
	[Export] public CharacterCatalog Catalog { get; set; }

	[Export] public Control TitleScreen { get; set; }
	/// <summary>The continue button and its caption; hidden without a run in progress.</summary>
	[Export] public Control ContinueRow { get; set; }
	[Export] public Button ContinueButton { get; set; }
	[Export] public Label ContinueCaption { get; set; }
	[Export] public Button NewRunButton { get; set; }
	[Export] public Button RunsButton { get; set; }
	[Export] public Button QuitButton { get; set; }

	[Export] public Control RunsScreen { get; set; }
	[Export] public Label RunsTitle { get; set; }
	/// <summary>Holds one <see cref="RunCard"/> per save slot, in slot order.</summary>
	[Export] public Container CardRow { get; set; }
	[Export] public Button RunsBackButton { get; set; }

	[Export] public CharacterPanel HeroScreen { get; set; }
	[Export] public DeathScreen DeathScreen { get; set; }
	[Export] public ConfirmDialog ReplaceDialog { get; set; }

	private static readonly StringName Cancel = "ui_cancel", Primary = "PrimaryButton";
	private RunCard[] _cards = [];
	/// <summary>The run Continue resumes: the most recently saved one in progress.</summary>
	private SaveSlotMetadata _latest;
	/// <summary>The slot the new run goes to; null until one is chosen, which with every slot taken follows the hero.</summary>
	private int? _pendingSlotId;
	/// <summary>The hero of a new run that still needs a slot: Runs is open to choose which run it replaces.</summary>
	private CharacterClass _pendingClass;
	/// <summary>Where backing out of the hero picker returns to.</summary>
	private Action _leaveHeroScreen;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		GetTree().Paused = false;
		_cards = CardRow.GetChildren().OfType<RunCard>().ToArray();

		ContinueButton.Pressed += () => GameSession.Instance.LoadGame(_latest.SlotId);
		NewRunButton.Pressed += StartNewRun;
		RunsButton.Pressed += ShowRuns;
		QuitButton.Pressed += () => GetTree().Quit();
		RunsBackButton.Pressed += LeaveRuns;
		foreach (RunCard card in _cards)
		{
			card.Pressed += () => SelectRun(card);
		}

		HeroScreen.ClassShown += Stage.ShowHero;
		HeroScreen.ClassConfirmed += ConfirmHero;
		HeroScreen.Canceled += () => _leaveHeroScreen();
		ReplaceDialog.Confirmed += () => GameSession.Instance.StartNewGame(_pendingSlotId.Value, _pendingClass.Id);
		ReplaceDialog.Canceled += () => _cards.FirstOrDefault(card => card.Slot?.SlotId == _pendingSlotId)?.GrabFocus();
		DeathScreen.Dismissed += () =>
		{
			DeathScreen.Close();
			ShowRuns();
		};

		ShowTitle();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionReleased(Cancel) && RunsScreen.Visible && !ReplaceDialog.Visible)
		{
			LeaveRuns();
			GetViewport().SetInputAsHandled();
		}
	}

	private void ShowTitle()
	{
		_pendingClass = null;
		IReadOnlyList<SaveSlotMetadata> slots = SaveService.ListSlots();
		_latest = slots.Where(slot => slot.CanLoad && slot.Outcome == RunOutcome.InProgress)
			.MaxBy(slot => slot.SavedAt ?? DateTime.MinValue);
		ContinueRow.Visible = _latest != null;
		ContinueCaption.Text = _latest == null ? "" : $"{ClassOf(_latest)?.DisplayName ?? "Hero"} · Depth {_latest.DungeonDepth}";
		// Without a run to continue, starting one is the screen's main action.
		NewRunButton.ThemeTypeVariation = _latest == null ? Primary : null;
		RunsButton.Visible = slots.Any(slot => slot.HasSave);
		Stage.ShowHero(ClassOf(_latest) ?? Catalog.DefaultClass);

		Show(TitleScreen);
		(_latest != null ? ContinueButton : NewRunButton).CallDeferred(Control.MethodName.GrabFocus);
	}

	/// <summary>A new run starts with its hero and takes the first free slot; with none free the slot is asked for afterwards.</summary>
	private void StartNewRun() =>
		ChooseHero(SaveService.ListSlots().FirstOrDefault(slot => !slot.HasSave || !slot.CanLoad)?.SlotId, ShowTitle);

	/// <summary>The hero is picked. With a slot to go to the run starts; otherwise the player says which run it replaces.</summary>
	private void ConfirmHero(CharacterClass characterClass)
	{
		if (_pendingSlotId is { } slotId)
		{
			GameSession.Instance.StartNewGame(slotId, characterClass.Id);
			return;
		}

		_pendingClass = characterClass;
		ShowRuns();
	}

	/// <summary>Back from Runs: to the hero waiting for its slot, or to the title.</summary>
	private void LeaveRuns()
	{
		if (_pendingClass != null)
		{
			_pendingClass = null;
			ChooseHero(null, ShowTitle);
		}
		else
		{
			ShowTitle();
		}
	}

	private void ShowRuns()
	{
		RunsTitle.Text = _pendingClass != null ? "Replace a Run" : "Runs";
		IReadOnlyList<SaveSlotMetadata> slots = SaveService.ListSlots();
		for (int index = 0; index < _cards.Length && index < slots.Count; index++)
		{
			_cards[index].Show(slots[index], ClassOf(slots[index]));
		}

		Show(RunsScreen);
		_cards.FirstOrDefault()?.CallDeferred(Control.MethodName.GrabFocus);
	}

	private void SelectRun(RunCard card)
	{
		SaveSlotMetadata slot = card.Slot;
		if (_pendingClass != null && !card.HoldsRun)
		{
			GameSession.Instance.StartNewGame(slot.SlotId, _pendingClass.Id);
		}
		else if (_pendingClass != null)
		{
			// The slot's file is only replaced by the new run's first save, so nothing is lost before this is confirmed.
			_pendingSlotId = slot.SlotId;
			ReplaceDialog.Ask("Replace run?", $"{card.NameLabel.Text} · Depth {slot.DungeonDepth}", "Replace");
		}
		else if (!card.HoldsRun)
		{
			ChooseHero(slot.SlotId, ShowRuns);
		}
		else if (slot.Outcome == RunOutcome.Died)
		{
			// A fallen run cannot be continued; its card shows how it ended.
			Show(null);
			DeathScreen.Open(slot, ClassOf(slot)?.DisplayName);
		}
		else
		{
			GameSession.Instance.LoadGame(slot.SlotId);
		}
	}

	/// <summary>Opens the hero picker for a run going to <paramref name="slotId"/>, or to a slot still to be chosen.</summary>
	private void ChooseHero(int? slotId, Action leave)
	{
		_pendingSlotId = slotId;
		_leaveHeroScreen = leave;
		Show(null);
		HeroScreen.Open();
	}

	/// <summary>Shows <paramref name="screen"/> alone; null hides every screen for one that manages itself.</summary>
	private void Show(Control screen)
	{
		HeroScreen.Close();
		TitleScreen.Visible = screen == TitleScreen;
		RunsScreen.Visible = screen == RunsScreen;
	}

	private CharacterClass ClassOf(SaveSlotMetadata slot) => slot == null ? null : Catalog?.FindById(slot.CharacterClassId);
}
