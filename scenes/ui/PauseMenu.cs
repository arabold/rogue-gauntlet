using Godot;

public partial class PauseMenu : Control
{
	[Export] public Control PausePanel { get; set; }
	[Export] public ColorRect Dim { get; set; }
	[Export] public Button AudioSettingsButton { get; set; }
	[Export] public AudioSettingsPanel AudioSettingsPanel { get; set; }

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
		if (PausePanel == null || Dim == null || AudioSettingsButton == null || AudioSettingsPanel == null)
		{
			GD.PushError("PauseMenu requires PausePanel, Dim, AudioSettingsButton and AudioSettingsPanel to be wired.");
		}
		else
		{
			AudioSettingsButton.Pressed += OpenAudioSettings;
			AudioSettingsPanel.Closed += CloseAudioSettings;
		}

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
			if (AudioSettingsPanel?.Visible == true)
			{
				AudioSettingsPanel.Close();
			}
			else if (Visible)
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

	private void OpenAudioSettings()
	{
		PausePanel.Visible = false;
		Dim.Visible = false;
		AudioSettingsPanel.Open();
	}

	private void CloseAudioSettings()
	{
		Dim.Visible = true;
		PausePanel.Visible = true;
		AudioSettingsButton.GrabFocus();
	}
}