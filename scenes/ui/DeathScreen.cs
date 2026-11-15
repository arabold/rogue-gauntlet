using Godot;
using System;

/// <summary>
/// Reports a fallen run: its class and the depth it reached, then its level, gold and time played. The gameplay scene opens it
/// when the hero dies, and the menu opens it for a slot that holds a fallen run, so both show the same record.
/// </summary>
public partial class DeathScreen : Control
{
	/// <summary>The player has read the record and pressed the dismiss button or cancel.</summary>
	[Signal] public delegate void DismissedEventHandler();

	/// <summary>Who fell and where: "Barbarian · Depth 3".</summary>
	[Export] public Label EpitaphLabel { get; set; }
	[Export] public Label LevelLabel { get; set; }
	[Export] public Label GoldLabel { get; set; }
	[Export] public Label TimeLabel { get; set; }
	[Export] public Button DismissButton { get; set; }
	/// <summary>The dismiss button's text, set per host: the game returns to the menu, the menu goes back to its slots.</summary>
	[Export] public string DismissText { get; set; } = "Main Menu";
	[Export(PropertyHint.Range, "0,3,0.05")] public float FadeInSeconds { get; set; } = 0.8f;

	private static readonly StringName CancelAction = "ui_cancel";
	private Tween _fade;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		DismissButton.Text = DismissText;
		DismissButton.Pressed += EmitSignalDismissed;
		Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Visible && @event.IsActionReleased(CancelAction))
		{
			GetViewport().SetInputAsHandled();
			EmitSignalDismissed();
		}
	}

	/// <summary>
	/// Shows <paramref name="run"/>'s record, fading in over whatever is behind it. The host names the run's class, since
	/// it already knows it; without a <paramref name="className"/> the record names none.
	/// </summary>
	public void Open(SaveSlotMetadata run, string className)
	{
		EpitaphLabel.Text = className != null ? $"{className} · Depth {run.DungeonDepth}" : $"Depth {run.DungeonDepth}";
		LevelLabel.Text = run.XpLevel.ToString();
		GoldLabel.Text = run.Gold.ToString();
		TimeLabel.Text = FormatPlayTime(run.PlayTimeSeconds);

		_fade?.Kill();
		Modulate = Colors.Transparent;
		Visible = true;
		_fade = CreateTween();
		_fade.TweenProperty(this, "modulate:a", 1f, FadeInSeconds);
		DismissButton.GrabFocus();
	}

	public void Close()
	{
		_fade?.Kill();
		Visible = false;
	}

	/// <summary>Play time as the shortest clock that holds it: "4:07", or "1:04:07" from the first hour.</summary>
	public static string FormatPlayTime(double seconds)
	{
		TimeSpan time = TimeSpan.FromSeconds(seconds);
		return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
	}
}
