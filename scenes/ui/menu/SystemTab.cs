using Godot;

/// <summary>
/// The run menu's last tab. There is no save on demand: leaving for the main menu saves the run, as do arriving on a
/// floor and closing the window.
/// </summary>
public partial class SystemTab : RunMenuTab
{
	[Export] public Button ResumeButton { get; set; }
	[Export] public Button SaveAndQuitButton { get; set; }
	[Export] public Control Menu { get; set; }
	[Export] public Button AudioSettingsButton { get; set; }
	[Export] public AudioSettingsPanel AudioSettings { get; set; }
	/// <summary>Reports a save that failed, which keeps the player in the game.</summary>
	[Export] public Label StatusLabel { get; set; }

	public override void _Ready()
	{
		AudioSettingsButton.Pressed += () =>
		{
			Menu.Hide();
			AudioSettings.Open();
		};
		AudioSettings.Closed += () =>
		{
			Menu.Show();
			if (IsVisibleInTree()) AudioSettingsButton.GrabFocus();
		};
		ResumeButton.Pressed += () => EmitSignalCloseRequested();
		SaveAndQuitButton.Pressed += SaveAndQuit;
	}

	public override void Open(Player player)
	{
		base.Open(player);
		StatusLabel.Text = "";
	}

	public override void FocusDefault() => ResumeButton.GrabFocus();

	/// <summary>Returns to system actions before the cancel input can dismiss the run menu.</summary>
	public override bool Cancel()
	{
		if (!AudioSettings.Visible) return false;
		AudioSettings.Close();
		return true;
	}

	public override void Close()
	{
		if (AudioSettings.Visible) AudioSettings.Close();
		base.Close();
	}

	private void SaveAndQuit()
	{
		if (GameSession.Instance?.SaveAndReturnToMenu() != true)
		{
			StatusLabel.Text = "Save failed.";
		}
	}
}