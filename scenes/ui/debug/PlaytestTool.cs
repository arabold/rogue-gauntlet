using Godot;

/// <summary>
/// Starts and stops a <see cref="PlaytestRecorder"/>, which logs a whole run across floors for the balance model's
/// <c>simulate.py playtest</c> report. Launching the game with <c>-- --playtest-log[=PATH]</c> starts one too. Needs a
/// generated <see cref="Level"/>.
/// </summary>
[GlobalClass]
public partial class PlaytestTool : DebugTool
{
	private static bool _commandLineHandled;
	private int _copyPathId;

	public override bool IsAvailable => Menu?.Level != null;

	/// <summary>Whether a playtest is being recorded.</summary>
	public bool Recording
	{
		get => PlaytestRecorder.Current != null;
		set
		{
			if (value == Recording) return;
			if (value) PlaytestRecorder.Start(GetTree(), Menu.Level);
			else PlaytestRecorder.Stop();
		}
	}

	public override void _Ready()
	{
		// Once per launch, so a playtest stopped from the menu stays stopped on the next floor.
		if (_commandLineHandled || !OS.IsDebugBuild()) return;
		_commandLineHandled = true;
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg == "--playtest-log" || arg.StartsWith("--playtest-log="))
				PlaytestRecorder.Start(GetTree(), Menu?.Level, arg.Contains('=') ? arg.Split('=', 2)[1] : "");
		}
	}

	protected override void BuildMenu(PopupMenu menu)
	{
		AddToggle(menu, "Record playtest", () => Recording, value => Recording = value);
		_copyPathId = AddAction(menu, "Copy log path", () => DisplayServer.ClipboardSet(PlaytestRecorder.Current?.OutputPath ?? ""));
	}

	protected override void OnRefresh() => SetDisabled(Popup, _copyPathId, !Recording);
}
