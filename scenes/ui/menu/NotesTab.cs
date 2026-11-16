using Godot;
using System;

/// <summary>
/// The run menu's Notes tab: a scrollable chronicle of every player log message emitted during the current run.
/// </summary>
[GlobalClass]
public partial class NotesTab : RunMenuTab, ILogSink
{
	private static readonly StringName ThemeDefaultColor = "default_color";

	[Export] public ScrollContainer Scroll { get; set; }
	[Export] public VBoxContainer Entries { get; set; }
	[Export] public Label EmptyHint { get; set; }
	[Export(PropertyHint.Range, "20,400,4")] public int ScrollStep { get; set; } = 64;

	private bool _isOpen;
	private bool _followNewest = true;
	private bool _applyingScroll;
	private int _syncedEpoch = -1;
	private int _syncedCount;

	public override void _Ready()
	{
		VScrollBar bar = Scroll?.GetVScrollBar();
		if (bar != null)
		{
			bar.Changed += OnScrollRangeChanged;
		}
		if (Scroll != null)
		{
			Scroll.ScrollStarted += OnManualScrollStarted;
			Scroll.GuiInput += OnScrollGuiInput;
		}
	}

	public override void _ExitTree()
	{
		GameLog.RemoveSink(this);
		base._ExitTree();
	}

	public override void Open(Player player)
	{
		base.Open(player);
		_isOpen = true;
		_followNewest = true;
		SyncHistory();
		GameLog.AddSink(this);
		ScrollToBottomDeferred();
	}

	public override void Close()
	{
		_isOpen = false;
		GameLog.RemoveSink(this);
		base.Close();
	}

	public override void FocusDefault()
	{
		Scroll?.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_isOpen || !Visible || Scroll == null)
		{
			return;
		}

		int delta = 0;
		if (@event.IsActionPressed("ui_down"))
		{
			delta = ScrollStep;
		}
		else if (@event.IsActionPressed("ui_up"))
		{
			delta = -ScrollStep;
		}
		else if (@event.IsActionPressed("ui_page_down"))
		{
			delta = Mathf.Max(ScrollStep, Mathf.RoundToInt(Scroll.Size.Y * 0.8f));
		}
		else if (@event.IsActionPressed("ui_page_up"))
		{
			delta = -Mathf.Max(ScrollStep, Mathf.RoundToInt(Scroll.Size.Y * 0.8f));
		}

		if (delta != 0)
		{
			ApplyScrollDelta(delta);
			GetViewport().SetInputAsHandled();
		}
	}

	public void Log(in LogEntry entry)
	{
		if (!_isOpen || entry.Channel != LogChannel.Player || !IsInsideTree())
		{
			return;
		}

		LogEntry captured = entry;
		if (OS.GetMainThreadId() == OS.GetThreadCallerId())
		{
			Deliver();
		}
		else
		{
			Callable.From(Deliver).CallDeferred();
		}

		void Deliver()
		{
			if (!GodotObject.IsInstanceValid(this) || !_isOpen || !IsInsideTree())
			{
				return;
			}

			if (!SyncHistory())
			{
				AppendEntry(in captured);
				UpdateEmptyHint();
				_syncedEpoch = -1;
			}
			_followNewest = true;
			ScrollToBottomDeferred();
		}
	}

	private bool SyncHistory()
	{
		if (Entries == null || !GameLog.TrySyncPlayerHistory(ref _syncedEpoch, ref _syncedCount, out LogEntry[] entries, out bool reset))
		{
			return false;
		}

		if (reset)
		{
			foreach (Node child in Entries.GetChildren())
			{
				Entries.RemoveChild(child);
				child.QueueFree();
			}
		}

		foreach (LogEntry entry in entries)
		{
			AppendEntry(in entry);
		}

		UpdateEmptyHint();
		return true;
	}

	private void AppendEntry(in LogEntry entry)
	{
		if (Entries == null || string.IsNullOrEmpty(entry.Message))
		{
			return;
		}

		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			MouseFilter = MouseFilterEnum.Pass,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Text = HudMessageFeed.FormatMessageBbcode(entry.Message, HudMessageFeed.DefaultKeywordColor),
		};
		label.AddThemeColorOverride(ThemeDefaultColor, HudMessageFeed.DefaultColorFor(entry.Severity));
		Entries.AddChild(label);
	}

	private void UpdateEmptyHint()
	{
		if (EmptyHint != null && Entries != null)
		{
			EmptyHint.Visible = Entries.GetChildCount() == 0;
		}
	}

	private void OnScrollRangeChanged()
	{
		if (_followNewest && _isOpen)
		{
			ScrollToBottom();
		}
	}

	private void OnManualScrollStarted() => UpdateFollowFromPosition();

	private void OnScrollGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown })
		{
			CallDeferred(MethodName.UpdateFollowFromPosition);
		}
	}

	private void ApplyScrollDelta(int delta)
	{
		if (Scroll == null)
		{
			return;
		}

		int maxScroll = MaxScroll();
		Scroll.ScrollVertical = Mathf.Clamp(Scroll.ScrollVertical + delta, 0, maxScroll);
		UpdateFollowFromPosition();
	}

	private void UpdateFollowFromPosition()
	{
		if (_applyingScroll || Scroll == null)
		{
			return;
		}

		_followNewest = Scroll.ScrollVertical >= MaxScroll() - 4;
	}

	private int MaxScroll()
	{
		VScrollBar bar = Scroll?.GetVScrollBar();
		return bar == null ? 0 : Mathf.Max(0, Mathf.CeilToInt(bar.MaxValue - bar.Page));
	}

	private void ScrollToBottomDeferred()
	{
		if (Scroll == null || !IsInsideTree())
		{
			return;
		}

		Callable.From(ScrollToBottom).CallDeferred();
	}

	private void ScrollToBottom()
	{
		if (!GodotObject.IsInstanceValid(Scroll) || !Scroll.IsInsideTree() || !_followNewest)
		{
			return;
		}

		_applyingScroll = true;
		try
		{
			Scroll.ScrollVertical = MaxScroll();
		}
		finally
		{
			_applyingScroll = false;
		}
	}
}