using Godot;

/// <summary>
/// The run menu's last tab. There is no save on demand: leaving for the main menu saves the run, as do arriving on a
/// floor and closing the window.
/// </summary>
public partial class SystemTab : RunMenuTab
{
	[Export] public Button ResumeButton { get; set; }
	[Export] public Button SaveAndQuitButton { get; set; }
	/// <summary>Reports a save that failed, which keeps the player in the game.</summary>
	[Export] public Label StatusLabel { get; set; }

	public override void _Ready()
	{
		ResumeButton.Pressed += () => EmitSignalCloseRequested();
		SaveAndQuitButton.Pressed += SaveAndQuit;
	}

	public override void Open(Player player)
	{
		base.Open(player);
		StatusLabel.Text = "";
	}

	public override void FocusDefault() => ResumeButton.GrabFocus();

	private void SaveAndQuit()
	{
		if (GameSession.Instance?.SaveAndReturnToMenu() != true)
		{
			StatusLabel.Text = "Save failed.";
		}
	}
}