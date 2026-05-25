using Godot;
using System;
using System.Linq;

public partial class MainMenu : Control
{
	private enum SlotMode
	{
		NewGame,
		LoadGame,
	}

	private Button _newGameButton;
	private Button _loadGameButton;
	private Button _quitButton;
	private Control _mainButtons;
	private Control _slotPanel;
	private Label _slotTitle;
	private Button _backButton;
	private ConfirmationDialog _overwriteDialog;
	private Button[] _slotButtons;
	private SlotMode _slotMode;
	private int _pendingSlotId;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		GetTree().Paused = false;

		_newGameButton = GetNode<Button>("%NewGameButton");
		_loadGameButton = GetNode<Button>("%LoadGameButton");
		_quitButton = GetNode<Button>("%QuitButton");
		_mainButtons = GetNode<Control>("%MainButtons");
		_slotPanel = GetNode<Control>("%SlotPanel");
		_slotTitle = GetNode<Label>("%SlotTitle");
		_backButton = GetNode<Button>("%BackButton");
		_overwriteDialog = GetNode<ConfirmationDialog>("%OverwriteDialog");
		_slotButtons = Enumerable.Range(1, SaveService.SlotCount)
			.Select(i => GetNode<Button>($"%Slot{i}Button"))
			.ToArray();
		_overwriteDialog.AboutToPopup += () => _overwriteDialog.GetOkButton().CallDeferred(Control.MethodName.GrabFocus);

		_newGameButton.Pressed += ShowNewGameSlots;
		_loadGameButton.Pressed += ShowLoadGameSlots;
		_quitButton.Pressed += () => GetTree().Quit();
		_backButton.Pressed += ShowMainButtons;
		_overwriteDialog.Confirmed += () => GameSession.Instance.StartNewGame(_pendingSlotId);

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
		_loadGameButton.Disabled = !SaveService.ListSlots().Any(slot => slot.HasSave);
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
		foreach (SaveSlotMetadata slot in SaveService.ListSlots())
		{
			Button button = _slotButtons[slot.SlotId - 1];
			button.Text = FormatSlotLabel(slot);
			button.Disabled = _slotMode == SlotMode.LoadGame && !slot.HasSave;
		}

		Button focusButton = _slotButtons.FirstOrDefault(button => !button.Disabled) ?? _backButton;
		focusButton.CallDeferred(Control.MethodName.GrabFocus);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionReleased("ui_cancel") && _slotPanel.Visible)
		{
			ShowMainButtons();
			GetViewport().SetInputAsHandled();
		}
	}

	private void SelectSlot(int slotId)
	{
		if (_slotMode == SlotMode.LoadGame)
		{
			GameSession.Instance.LoadGame(slotId);
			return;
		}

		if (SaveService.HasSave(slotId))
		{
			_pendingSlotId = slotId;
			_overwriteDialog.DialogText = $"Slot {slotId} already has a game. Start a new game and overwrite it?";
			_overwriteDialog.PopupCentered();
			return;
		}

		GameSession.Instance.StartNewGame(slotId);
	}

	private static string FormatSlotLabel(SaveSlotMetadata slot)
	{
		if (!slot.HasSave)
		{
			return $"Slot {slot.SlotId}\nEmpty";
		}

		string savedAt = FormatSavedAt(slot.SavedAtUtc);
		string playTime = TimeSpan.FromSeconds(slot.PlayTimeSeconds).ToString(@"h\:mm\:ss");
		return $"Slot {slot.SlotId}\nDepth {slot.DungeonDepth}  Level {slot.XpLevel}  Gold {slot.Gold}\n{playTime}  {savedAt}";
	}

	private static string FormatSavedAt(string savedAtUtc)
	{
		return DateTime.TryParse(savedAtUtc, out DateTime savedAt)
			? savedAt.ToLocalTime().ToString("g")
			: "Unknown date";
	}
}
