using Godot;
using System;

/// <summary>
/// Reports a fallen run: its class, the depth it reached, its level, gold and time played. The gameplay scene opens it
/// when the hero dies, and the menu opens it for a slot that holds a fallen run, so both show the same record.
/// </summary>
public partial class DeathScreen : Control
{
	/// <summary>The player has read the record and pressed the dismiss button or cancel.</summary>
	[Signal] public delegate void DismissedEventHandler();

	[Export] public Label EpitaphLabel { get; set; }
	[Export] public Label DetailsLabel { get; set; }
	[Export] public Button DismissButton { get; set; }
	/// <summary>The dismiss button's text, set per host: the game returns to the menu, the menu goes back to its slots.</summary>
	[Export] public string DismissText { get; set; } = "Return to Main Menu";
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
		EpitaphLabel.Text = className != null
			? $"The {className} fell on depth {run.DungeonDepth}."
			: $"Fell on depth {run.DungeonDepth}.";
		DetailsLabel.Text = $"Level {run.XpLevel}    Gold {run.Gold}\nTime played {TimeSpan.FromSeconds(run.PlayTimeSeconds):h\\:mm\\:ss}";

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
}
