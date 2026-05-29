using Godot;

public partial class PauseMenu : Control
{
	private Button _resumeButton;
	private Button _saveButton;
	private Button _saveAndQuitButton;
	private Button _quitWithoutSavingButton;
	private Label _statusLabel;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_resumeButton = GetNode<Button>("%ResumeButton");
		_saveButton = GetNode<Button>("%SaveButton");
		_saveAndQuitButton = GetNode<Button>("%SaveAndQuitButton");
		_quitWithoutSavingButton = GetNode<Button>("%QuitWithoutSavingButton");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_resumeButton.Pressed += Close;
		_saveButton.Pressed += Save;
		_saveAndQuitButton.Pressed += () => GameSession.Instance.SaveAndReturnToMenu();
		_quitWithoutSavingButton.Pressed += () => GameSession.Instance.ReturnToMenu();

		Visible = false;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Echo: true })
		{
			return;
		}

		if (@event.IsActionReleased("pause"))
		{
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
	}

	private void Open()
	{
		_statusLabel.Text = string.Empty;
		Visible = true;
		GetTree().Paused = true;
		SignalBus.EmitGamePaused(true);
		_resumeButton.GrabFocus();
	}

	private void Close()
	{
		Visible = false;
		GetTree().Paused = false;
		SignalBus.EmitGamePaused(false);
	}

	private void Save()
	{
		_statusLabel.Text = GameSession.Instance.SaveActiveGame()
			? "Saved."
			: "Save failed.";
	}
}
