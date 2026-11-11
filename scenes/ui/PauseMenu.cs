using Godot;

/// <summary>
/// Pauses the game. There is no save on demand: leaving for the menu saves the run, and so do arriving on a floor and
/// closing the window. A dead hero cannot pause; the death screen takes over.
/// </summary>
public partial class PauseMenu : Control
{
	[Export] public Button ResumeButton { get; set; }
	[Export] public Button SaveAndQuitButton { get; set; }
	/// <summary>Reports a save that failed, which keeps the player in the game.</summary>
	[Export] public Label StatusLabel { get; set; }

	private static readonly StringName PauseAction = "pause";

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		ResumeButton.Pressed += Close;
		SaveAndQuitButton.Pressed += SaveAndQuit;
		Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Echo: true } || !@event.IsActionReleased(PauseAction)
			|| !Visible && GetTree().GetFirstNodeInGroup("player") is Player { IsDead: true })
		{
			return;
		}

		if (Visible)
		{
			Close();
		}
		else
		{
			Open();
		}
		GetViewport().SetInputAsHandled();
	}

	private void Open()
	{
		StatusLabel.Text = string.Empty;
		Visible = true;
		GetTree().Paused = true;
		SignalBus.EmitGamePaused(true);
		ResumeButton.GrabFocus();
	}

	private void Close()
	{
		Visible = false;
		GetTree().Paused = false;
		SignalBus.EmitGamePaused(false);
	}

	private void SaveAndQuit()
	{
		if (!GameSession.Instance.SaveAndReturnToMenu())
		{
			StatusLabel.Text = "Save failed.";
		}
	}
}
