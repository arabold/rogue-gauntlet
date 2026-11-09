using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]
public partial class HudMessageFeed : VBoxContainer, ILogSink
{
	private static readonly StringName ThemeDefaultColor = "default_color";
	private static readonly StringName ThemeFontShadowColor = "font_shadow_color";
	private static readonly StringName ThemeShadowOffsetX = "shadow_offset_x";
	private static readonly StringName ThemeShadowOffsetY = "shadow_offset_y";
	private static readonly StringName ThemeShadowOutlineSize = "shadow_outline_size";
	private static readonly Color FontShadowColor = new(0, 0, 0, 0.85f);

	[Export(PropertyHint.Range, "1,10,1")] public int MaxVisibleMessages { get; set; } = 5;
	[Export(PropertyHint.Range, "1,15,0.5")] public float MessageDurationSeconds { get; set; } = 5.0f;
	[Export(PropertyHint.Range, "0.1,3,0.05")] public float FadeDurationSeconds { get; set; } = 0.75f;

	[Export] public Color InfoColor { get; set; } = new(0.89f, 0.91f, 0.94f); // #E2E8F0 Soft Silver
	[Export] public Color DiscoveryColor { get; set; } = new(0.22f, 0.74f, 0.97f); // #38BDF8 Electric Cyan
	[Export] public Color ExperienceColor { get; set; } = new(0.75f, 0.52f, 0.99f); // #C084FC Arcane Lilac
	[Export] public Color GoldColor { get; set; } = new(0.98f, 0.8f, 0.08f); // #FACC15 Warm Gold
	[Export] public Color ItemPickupColor { get; set; } = new(0.43f, 0.91f, 0.72f); // #6EE7B7 Mint Jade
	[Export] public Color EquipmentColor { get; set; } = new(0.58f, 0.77f, 0.99f); // #93C5FD Steel Blue
	[Export] public Color WarningColor { get; set; } = new(0.98f, 0.57f, 0.24f); // #FB923C Amber Orange
	[Export] public Color ErrorColor { get; set; } = new(0.97f, 0.44f, 0.44f); // #F87171 Soft Red
	[Export] public Color KeywordColor { get; set; } = new(1f, 1f, 1f); // Bright White

	private sealed class FeedItem
	{
		public RichTextLabel Label { get; init; }
		public string OriginalText { get; init; }
		public LogSeverity Severity { get; init; }
		public int RepeatCount { get; set; } = 1;
		public float RemainingSeconds { get; set; }

		public string DisplayText => RepeatCount > 1 ? $"{OriginalText} (x{RepeatCount})" : OriginalText;
	}

	private readonly List<FeedItem> _items = new();
	private bool _isPaused;

	public int ActiveMessageCount => _items.Count;

	public IReadOnlyList<string> GetActiveMessages()
	{
		var list = new List<string>(_items.Count);
		foreach (FeedItem item in _items)
		{
			list.Add(item.DisplayText);
		}
		return list;
	}

	public override void _EnterTree()
	{
		base._EnterTree();
		SetProcess(false);
		MouseFilter = MouseFilterEnum.Ignore;
		GameLog.AddSink(this);

		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.GamePaused += OnGamePaused,
			bus => bus.GamePaused -= OnGamePaused);
	}

	public override void _ExitTree()
	{
		GameLog.RemoveSink(this);
		base._ExitTree();
	}

	private void OnGamePaused(bool isPaused)
	{
		_isPaused = isPaused;
	}

	public void Log(in LogEntry entry)
	{
		if (entry.Channel != LogChannel.Player)
		{
			return;
		}

		if (!IsInsideTree())
		{
			return;
		}

		string message = entry.Message;
		LogSeverity severity = entry.Severity;

		if (OS.GetMainThreadId() == OS.GetThreadCallerId())
		{
			AddMessage(message, severity);
		}
		else
		{
			Callable.From(() => AddMessage(message, severity)).CallDeferred();
		}
	}

	public void AddMessage(string text, LogSeverity severity = LogSeverity.Info)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		// Coalesce repeated messages if the newest message matches
		if (_items.Count > 0)
		{
			FeedItem latest = _items[^1];
			if (latest.OriginalText == text && latest.Severity == severity && latest.RemainingSeconds > 0)
			{
				latest.RepeatCount++;
				latest.RemainingSeconds = MessageDurationSeconds;
				latest.Label.Text = FormatBbcode(latest.DisplayText);
				latest.Label.Modulate = Colors.White;
				return;
			}
		}

		Color color = severity switch
		{
			LogSeverity.Discovery => DiscoveryColor,
			LogSeverity.Experience => ExperienceColor,
			LogSeverity.Gold => GoldColor,
			LogSeverity.ItemPickup => ItemPickupColor,
			LogSeverity.Equipment => EquipmentColor,
			LogSeverity.Warning => WarningColor,
			LogSeverity.Error => ErrorColor,
			_ => InfoColor
		};

		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			MouseFilter = MouseFilterEnum.Ignore,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};

		// Outline and shadow for high contrast over 3D backgrounds
		label.AddThemeColorOverride(ThemeDefaultColor, color);
		label.AddThemeColorOverride(ThemeFontShadowColor, FontShadowColor);
		label.AddThemeConstantOverride(ThemeShadowOffsetX, 1);
		label.AddThemeConstantOverride(ThemeShadowOffsetY, 1);
		label.AddThemeConstantOverride(ThemeShadowOutlineSize, 2);

		label.Text = FormatBbcode(text);
		AddChild(label);

		var item = new FeedItem
		{
			Label = label,
			OriginalText = text,
			Severity = severity,
			RemainingSeconds = MessageDurationSeconds
		};

		if (_items.Count == 0)
		{
			SetProcess(true);
		}

		_items.Add(item);

		while (_items.Count > MaxVisibleMessages)
		{
			FeedItem oldest = _items[0];
			_items.RemoveAt(0);
			oldest.Label.QueueFree();
		}
	}

	private string FormatBbcode(string text)
	{
		if (string.IsNullOrEmpty(text) || !text.Contains("[b]", StringComparison.Ordinal))
		{
			return text ?? "";
		}

		string hex = KeywordColor.ToHtml(false);
		return text
			.Replace("[b]", $"[b][color=#{hex}]", StringComparison.Ordinal)
			.Replace("[/b]", "[/color][/b]", StringComparison.Ordinal);
	}

	public override void _Process(double delta)
	{
		if (_isPaused || _items.Count == 0)
		{
			return;
		}

		float deltaFloat = (float)delta;
		for (int i = _items.Count - 1; i >= 0; i--)
		{
			FeedItem item = _items[i];
			item.RemainingSeconds -= deltaFloat;

			if (item.RemainingSeconds <= 0)
			{
				_items.RemoveAt(i);
				item.Label.QueueFree();
			}
			else if (item.RemainingSeconds <= FadeDurationSeconds && FadeDurationSeconds > 0)
			{
				float alpha = Mathf.Clamp(item.RemainingSeconds / FadeDurationSeconds, 0f, 1f);
				item.Label.Modulate = new Color(1f, 1f, 1f, alpha);
			}
		}

		if (_items.Count == 0)
		{
			SetProcess(false);
		}
	}

	/// <summary>
	/// Clear all active feed messages immediately (used for cleanup and testing).
	/// </summary>
	public void Clear()
	{
		foreach (FeedItem item in _items)
		{
			item.Label.QueueFree();
		}
		_items.Clear();
		SetProcess(false);
	}
}