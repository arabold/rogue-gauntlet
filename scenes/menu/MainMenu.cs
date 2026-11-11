using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MainMenu : Control
{
	private enum SlotMode
	{
		NewGame,
		LoadGame,
	}

	/// <summary>Shows the record of a slot whose hero has died, the same screen the death itself showed.</summary>
	[Export] public DeathScreen DeathScreen { get; set; }

	private Button _newGameButton;
	private Button _loadGameButton;
	private Button _quitButton;
	private Control _menuPanel;
	private Control _mainButtons;
	private Control _slotPanel;
	private Label _slotTitle;
	private Button _backButton;
	private ConfirmationDialog _overwriteDialog;
	private CharacterPanel _characterPanel;
	private Button[] _slotButtons;
	private SlotMode _slotMode;
	/// <summary>The slots as the panel last listed them, indexed by slot id - 1.</summary>
	private IReadOnlyList<SaveSlotMetadata> _slots = [];
	private int _pendingSlotId;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		GetTree().Paused = false;

		_newGameButton = GetNode<Button>("%NewGameButton");
		_loadGameButton = GetNode<Button>("%LoadGameButton");
		_quitButton = GetNode<Button>("%QuitButton");
		_menuPanel = GetNode<Control>("MenuPanel");
		_mainButtons = GetNode<Control>("%MainButtons");
		_slotPanel = GetNode<Control>("%SlotPanel");
		_slotTitle = GetNode<Label>("%SlotTitle");
		_backButton = GetNode<Button>("%BackButton");
		_overwriteDialog = GetNode<ConfirmationDialog>("%OverwriteDialog");
		_characterPanel = GetNode<CharacterPanel>("%CharacterPanel");
		_slotButtons = Enumerable.Range(1, SaveService.SlotCount)
			.Select(i => GetNode<Button>($"%Slot{i}Button"))
			.ToArray();
		_overwriteDialog.AboutToPopup += () => _overwriteDialog.GetOkButton().CallDeferred(Control.MethodName.GrabFocus);

		_newGameButton.Pressed += ShowNewGameSlots;
		_loadGameButton.Pressed += ShowLoadGameSlots;
		_quitButton.Pressed += () => GetTree().Quit();
		_backButton.Pressed += ShowMainButtons;
		// Overwrite is confirmed before class selection, but the slot file is only
		// actually replaced by the first save after StartNewGame — so backing out of
		// the character panel loses nothing.
		_overwriteDialog.Confirmed += ShowCharacterPanel;
		_characterPanel.ClassConfirmed += characterClass =>
			GameSession.Instance.StartNewGame(_pendingSlotId, characterClass.Id);
		_characterPanel.Canceled += () =>
		{
			_characterPanel.Close();
			_menuPanel.Visible = true;
			ShowNewGameSlots();
		};
		DeathScreen.Dismissed += () =>
		{
			DeathScreen.Close();
			_menuPanel.Visible = true;
			ShowLoadGameSlots();
		};

		for (int i = 0; i < _slotButtons.Length; i++)
		{
			int slotId = i + 1;
			_slotButtons[i].Pressed += () => SelectSlot(slotId);
		}

		ShowMainButtons();
	}

	private void ShowMainButtons()
	{
		_mainButtons.Visible = true;
		_slotPanel.Visible = false;
		_loadGameButton.Disabled = !SaveService.ListSlots().Any(slot => slot.CanLoad);
		_newGameButton.CallDeferred(Control.MethodName.GrabFocus);
	}

	private void ShowNewGameSlots()
	{
		_slotMode = SlotMode.NewGame;
		_slotTitle.Text = "Choose a Slot for New Game";
		ShowSlots();
	}

	private void ShowLoadGameSlots()
	{
		_slotMode = SlotMode.LoadGame;
		_slotTitle.Text = "Load Game";
		ShowSlots();
	}

	private void ShowSlots()
	{
		_mainButtons.Visible = false;
		_slotPanel.Visible = true;
		_slots = SaveService.ListSlots();
		foreach (SaveSlotMetadata slot in _slots)
		{
			Button button = _slotButtons[slot.SlotId - 1];
			button.Text = FormatSlotLabel(slot);
			button.Disabled = _slotMode == SlotMode.LoadGame && !slot.CanLoad;
		}

		Button focusButton = _slotButtons.FirstOrDefault(button => !button.Disabled) ?? _backButton;
		focusButton.CallDeferred(Control.MethodName.GrabFocus);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// IsVisibleInTree: the slot panel stays Visible while the character panel hides
		// the whole MenuPanel, and the character panel consumes its own cancel first.
		if (@event.IsActionReleased("ui_cancel") && _slotPanel.IsVisibleInTree())
		{
			ShowMainButtons();
			GetViewport().SetInputAsHandled();
		}
	}

	private void SelectSlot(int slotId)
	{
		SaveSlotMetadata slot = _slots[slotId - 1];
		if (_slotMode == SlotMode.LoadGame)
		{
			// A fallen run cannot be continued; loading it shows how it ended.
			if (slot.Outcome == RunOutcome.Died)
			{
				_menuPanel.Visible = false;
				DeathScreen.Open(slot, ClassName(slot));
				return;
			}
			GameSession.Instance.LoadGame(slotId);
			return;
		}

		_pendingSlotId = slotId;
		if (slot.HasSave)
		{
			_overwriteDialog.DialogText = slot.Outcome == RunOutcome.Died
				? $"Slot {slotId} holds a fallen hero's record. Start a new game and overwrite it?"
				: $"Slot {slotId} already has a game. Start a new game and overwrite it?";
			_overwriteDialog.PopupCentered();
			return;
		}

		ShowCharacterPanel();
	}

	private void ShowCharacterPanel()
	{
		_menuPanel.Visible = false;
		_characterPanel.Open();
	}

	private string FormatSlotLabel(SaveSlotMetadata slot)
	{
		if (slot.HasSave && !slot.CanLoad)
		{
			return $"Slot {slot.SlotId} — incompatible or unreadable save (new run required)";
		}
		if (!slot.HasSave)
		{
			return $"Slot {slot.SlotId}\nEmpty";
		}

		string className = ClassName(slot);
		string title = className != null ? $"Slot {slot.SlotId} - {className}" : $"Slot {slot.SlotId}";
		if (slot.Outcome == RunOutcome.Died)
		{
			title += " (fallen)";
		}
		string savedAt = FormatSavedAt(slot.SavedAtUtc);
		string playTime = TimeSpan.FromSeconds(slot.PlayTimeSeconds).ToString(@"h\:mm\:ss");
		return $"{title}\nDepth {slot.DungeonDepth}  Level {slot.XpLevel}  Gold {slot.Gold}\n{playTime}  {savedAt}";
	}

	private string ClassName(SaveSlotMetadata slot) => _characterPanel.Catalog?.FindById(slot.CharacterClassId)?.DisplayName;

	private static string FormatSavedAt(string savedAtUtc)
	{
		return DateTime.TryParse(savedAtUtc, out DateTime savedAt)
			? savedAt.ToLocalTime().ToString("g")
			: "Unknown date";
	}
}