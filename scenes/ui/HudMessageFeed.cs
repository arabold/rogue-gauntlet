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
	/// <summary>How each line sits across the feed's width; the HUD centers them under the hero.</summary>
	[Export] public HorizontalAlignment TextAlignment { get; set; } = HorizontalAlignment.Left;
	[Export(PropertyHint.Range, "1,15,0.5")] public float MessageDurationSeconds { get; set; } = 5.0f;
	[Export(PropertyHint.Range, "0.1,3,0.05")] public float FadeDurationSeconds { get; set; } = 0.75f;

	public static readonly Color DefaultInfoColor = new(0.89f, 0.91f, 0.94f); // #E2E8F0 Soft Silver
	public static readonly Color DefaultDiscoveryColor = new(0.22f, 0.74f, 0.97f); // #38BDF8 Electric Cyan
	public static readonly Color DefaultExperienceColor = new(0.75f, 0.52f, 0.99f); // #C084FC Arcane Lilac
	public static readonly Color DefaultGoldColor = new(0.98f, 0.8f, 0.08f); // #FACC15 Warm Gold
	public static readonly Color DefaultItemPickupColor = new(0.43f, 0.91f, 0.72f); // #6EE7B7 Mint Jade
	public static readonly Color DefaultEquipmentColor = new(0.58f, 0.77f, 0.99f); // #93C5FD Steel Blue
	public static readonly Color DefaultWarningColor = new(0.98f, 0.57f, 0.24f); // #FB923C Amber Orange
	public static readonly Color DefaultErrorColor = new(0.97f, 0.44f, 0.44f); // #F87171 Soft Red
	public static readonly Color DefaultKeywordColor = new(1f, 1f, 1f); // Bright White

	[Export] public Color InfoColor { get; set; } = DefaultInfoColor;
	[Export] public Color DiscoveryColor { get; set; } = DefaultDiscoveryColor;
	[Export] public Color ExperienceColor { get; set; } = DefaultExperienceColor;
	[Export] public Color GoldColor { get; set; } = DefaultGoldColor;
	[Export] public Color ItemPickupColor { get; set; } = DefaultItemPickupColor;
	[Export] public Color EquipmentColor { get; set; } = DefaultEquipmentColor;
	[Export] public Color WarningColor { get; set; } = DefaultWarningColor;
	[Export] public Color ErrorColor { get; set; } = DefaultErrorColor;
	[Export] public Color KeywordColor { get; set; } = DefaultKeywordColor;

	public static Color DefaultColorFor(LogSeverity severity) => severity switch
	{
		LogSeverity.Discovery => DefaultDiscoveryColor,
		LogSeverity.Experience => DefaultExperienceColor,
		LogSeverity.Gold => DefaultGoldColor,
		LogSeverity.ItemPickup => DefaultItemPickupColor,
		LogSeverity.Equipment => DefaultEquipmentColor,
		LogSeverity.Warning => DefaultWarningColor,
		LogSeverity.Error => DefaultErrorColor,
		_ => DefaultInfoColor,
	};

	public static string FormatMessageBbcode(string text, Color keywordColor)
	{
		if (string.IsNullOrEmpty(text) || !text.Contains("[b]", StringComparison.Ordinal))
		{
			return text ?? "";
		}

		string hex = keywordColor.ToHtml(false);
		return text
			.Replace("[b]", $"[b][color=#{hex}]", StringComparison.Ordinal)
			.Replace("[/b]", "[/color][/b]", StringComparison.Ordinal);
	}

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
		ProcessMode = ProcessModeEnum.Always;
		ZIndex = 20;
		SetProcess(false);
		MouseFilter = MouseFilterEnum.Ignore;
		GameLog.AddSink(this);
	}

	public override void _ExitTree()
	{
		GameLog.RemoveSink(this);
		base._ExitTree();
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
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(this) && IsInsideTree())
				{
					AddMessage(message, severity);
				}
			}).CallDeferred();
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
			HorizontalAlignment = TextAlignment,
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

	private string FormatBbcode(string text) => FormatMessageBbcode(text, KeywordColor);

	public override void _Process(double delta)
	{
		if (_items.Count == 0)
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