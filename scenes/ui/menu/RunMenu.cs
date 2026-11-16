using Godot;
using System;
using System.Linq;

public enum RunMenuPage
{
	Gear,
	Pack,
	Notes,
	System,
}

/// <summary>
/// The one menu of a run: Gear, Pack, Notes and System as tabs under the header band, over the paused and darkened game. The
/// inventory key opens Gear and the pause key System; a dead hero cannot open it, the death screen takes over. The
/// open tab says what it offers for the selection. The menu draws that as the key row, whose keys act at once, and
/// as a popup on a chosen slot, which the accept key and a click both open: choosing an item always shows what can
/// be done with it before anything happens.
/// </summary>
public partial class RunMenu : Control
{
	[ExportGroup("Tabs")]
	[Export] public RunMenuTab GearTab { get; set; }
	[Export] public RunMenuTab PackTab { get; set; }
	[Export] public RunMenuTab NotesTab { get; set; }
	[Export] public RunMenuTab SystemTab { get; set; }
	[Export] public Button GearButton { get; set; }
	[Export] public Button PackButton { get; set; }
	[Export] public Button NotesButton { get; set; }
	[Export] public Button SystemButton { get; set; }

	[ExportGroup("Readouts")]
	[Export] public Label GoldLabel { get; set; }
	/// <summary>One line under the header band: what a scroll asks for, or why something did not happen.</summary>
	[Export] public Label Banner { get; set; }
	[Export(PropertyHint.Range, "0.5,5,0.1,suffix:s")] public float NoticeSeconds { get; set; } = 1.5f;
	[Export] public HBoxContainer PromptRow { get; set; }
	[Export] public PackedScene PromptScene { get; set; }
	/// <summary>Lists what the open tab offers for a clicked slot.</summary>
	[Export] public PopupMenu ActionMenu { get; set; }

	private static readonly StringName InventoryAction = "inventory", PauseAction = "pause", CancelAction = "ui_cancel",
		AcceptAction = "ui_accept",
		PreviousTabAction = "menu_previous_tab", NextTabAction = "menu_next_tab";

	private RunMenuTab[] _tabs;
	private Button[] _buttons;
	private int _current = -1;
	private Player _player;
	private MenuPrompt[] _prompts = [];
	private Action _unsubscribeStats = () => { };
	private Tween _noticeTween;
	private string _heldNotice = "";

	public bool IsOpen => _current >= 0;
	public RunMenuPage? Page => IsOpen ? (RunMenuPage)_current : null;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;
		_tabs = [GearTab, PackTab, NotesTab, SystemTab];
		_buttons = [GearButton, PackButton, NotesButton, SystemButton];
		for (int i = 0; i < _tabs.Length; i++)
		{
			int index = i;
			_tabs[i].Visible = false;
			_tabs[i].PromptsChanged += () => { if (index == _current) { RefreshPrompts(); } };
			_tabs[i].Notified += Notify;
			_tabs[i].CloseRequested += Close;
			_tabs[i].ActionsRequested += anchor => { if (index == _current) { OfferActions(anchor); } };
			_buttons[i].Pressed += () => ShowTab(index);
		}
		ActionMenu.IndexPressed += index => _tabs[_current].Invoke(ActionMenu.GetItemMetadata((int)index).AsStringName());
	}

	public override void _ExitTree()
	{
		// Whatever runs after this scene must not inherit the menu's pause.
		if (IsOpen)
		{
			GetTree().Paused = false;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Echo: true })
		{
			return;
		}

		if (!IsOpen)
		{
			RunMenuPage? page = @event.IsActionPressed(InventoryAction) ? RunMenuPage.Gear
				: @event.IsActionPressed(PauseAction) ? RunMenuPage.System : null;
			// Not while something else holds the game (the death screen) or the depth card is up for a floor change.
			if (page is { } opened && !GetTree().Paused && LoadingScreen.Instance?.IsCovering != true
				&& GetTree().GetFirstNodeInGroup("player") is Player { IsDead: false } player)
			{
				Open(player, opened);
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		// Opening and closing both answer the press: the popup closes on the cancel key's press too, so its release
		// must not close the menu behind it.
		if (@event.IsActionPressed(InventoryAction) || @event.IsActionPressed(PauseAction) || @event.IsActionPressed(CancelAction))
		{
			CancelOrClose();
		}
		else if (@event.IsActionPressed(PreviousTabAction))
		{
			ShowTab((_current + _tabs.Length - 1) % _tabs.Length);
		}
		else if (@event.IsActionPressed(NextTabAction))
		{
			ShowTab((_current + 1) % _tabs.Length);
		}
		else if (_prompts.SelectMany(prompt => prompt.Actions).FirstOrDefault(action => @event.IsActionPressed(action)) is { } action)
		{
			Do(action);
		}
		else
		{
			return;
		}
		GetViewport().SetInputAsHandled();
	}

	/// <summary>Pauses the game and shows <paramref name="page"/> for <paramref name="player"/>; already open, it turns to that tab.</summary>
	public void Open(Player player, RunMenuPage page)
	{
		if (!IsOpen)
		{
			_player = player;
			Visible = true;
			GetTree().Paused = true;
			SignalBus.EmitGamePaused(true);
			_unsubscribeStats = this.SubscribeUntilExit(player.Stats, stats => stats.Changed += RefreshGold, stats => stats.Changed -= RefreshGold);
			RefreshGold();
		}
		ShowTab((int)page);
	}

	public void Close()
	{
		if (!IsOpen)
		{
			return;
		}

		LeaveTab();
		_unsubscribeStats();
		_unsubscribeStats = () => { };
		_player = null;
		GetViewport().GuiReleaseFocus();
		Visible = false;
		GetTree().Paused = false;
		SignalBus.EmitGamePaused(false);
	}

	private void ShowTab(int index)
	{
		LeaveTab();
		_current = index;
		for (int i = 0; i < _buttons.Length; i++)
		{
			_buttons[i].SetPressedNoSignal(i == index);
		}
		_tabs[index].Visible = true;
		_tabs[index].Open(_player);
		_tabs[index].FocusDefault();
		RefreshPrompts();
	}

	private void LeaveTab()
	{
		if (!IsOpen)
		{
			return;
		}

		ActionMenu.Hide();
		_tabs[_current].Close();
		_tabs[_current].Visible = false;
		_current = -1;
		Notify("", false);
	}

	private void CancelOrClose()
	{
		if (!_tabs[_current].Cancel())
		{
			Close();
		}
	}

	private void RefreshGold() => GoldLabel.Text = _player.Stats.Gold.ToString();

	private void RefreshPrompts()
	{
		RunMenuTab tab = _tabs[_current];
		_prompts = [.. tab.Prompts];

		// The accept key opens the list of what can be done, so the row names that rather than the list's first entry.
		var row = _prompts
			.Select(prompt => tab.AsksWhatToDo && prompt.Actions is [var only] && only == AcceptAction ? new MenuPrompt("Select", AcceptAction) : prompt)
			.Append(new MenuPrompt(tab.CancelLabel, PauseAction));
		// The row changes with every slot the pointer crosses, so its buttons stay and are relabelled.
		int shown = 0;
		foreach (MenuPrompt prompt in row)
		{
			MenuPromptButton button;
			if (shown < PromptRow.GetChildCount())
			{
				button = PromptRow.GetChild<MenuPromptButton>(shown);
			}
			else
			{
				button = PromptScene.Instantiate<MenuPromptButton>();
				PromptRow.AddChild(button);
				button.Pressed += OnPromptPressed;
			}
			button.Display(prompt);
			button.Visible = true;
			shown++;
		}
		for (; shown < PromptRow.GetChildCount(); shown++)
		{
			PromptRow.GetChild<Control>(shown).Visible = false;
		}
	}

	/// <summary>Pops up what the open tab offers for its selection, on the clicked slot. A range lists each of its slots.</summary>
	private void OfferActions(Control anchor)
	{
		ActionMenu.Clear();
		foreach (MenuPrompt prompt in _prompts)
		{
			for (int i = 0; i < prompt.Actions.Length; i++)
			{
				ActionMenu.AddItem(prompt.Actions.Length > 1 ? $"{prompt.Label} {i + 1}" : prompt.Label);
				ActionMenu.SetItemMetadata(ActionMenu.ItemCount - 1, prompt.Actions[i]);
			}
		}

		if (ActionMenu.ItemCount > 0)
		{
			ActionMenu.ResetSize();
			// From the slot's middle, so the item stays in view beside the list; the window keeps it on screen.
			ActionMenu.Popup(new Rect2I((Vector2I)anchor.GetGlobalRect().GetCenter(), Vector2I.Zero));
			// The first entry is what the item is mostly chosen for; the keys start on it.
			ActionMenu.SetFocusedItem(0);
		}
	}

	private void OnPromptPressed(StringName action)
	{
		if (action == PauseAction)
		{
			CancelOrClose();
		}
		else
		{
			Do(action);
		}
	}

	/// <summary>A key or a click on the key row: the accept key chooses the selection, every other acts on it at once.</summary>
	private void Do(StringName action)
	{
		if (action == AcceptAction)
		{
			_tabs[_current].Activate();
		}
		else
		{
			_tabs[_current].Invoke(action);
		}
	}

	/// <summary>Shows a line in the banner. A held line stays until an empty one clears it; any other gives way to it again.</summary>
	private void Notify(string message, bool held)
	{
		_noticeTween?.Kill();
		if (held || message.Length == 0)
		{
			_heldNotice = message;
		}

		Banner.Text = message;
		Banner.Visible = message.Length > 0;
		if (held || message.Length == 0)
		{
			return;
		}

		_noticeTween = CreateTween();
		_noticeTween.TweenInterval(NoticeSeconds);
		_noticeTween.TweenCallback(Callable.From(() =>
		{
			Banner.Text = _heldNotice;
			Banner.Visible = _heldNotice.Length > 0;
		}));
	}
}