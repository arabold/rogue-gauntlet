using Godot;
using System.Globalization;

/// <summary>
/// Floor diagnostics and run controls: the navigation overlay, darkness, an arena's walls, revealing the map,
/// restarting or changing the floor and replaying the run from a seed. Floor travel and the run seed need a generated
/// <see cref="Level"/>; darkness and navigation need a debug world.
/// </summary>
[GlobalClass]
public partial class LevelTool : DebugTool
{
	private NavigationDebugOverlay _navigationOverlay;
	private ConfirmationDialog _replayDialog;
	private LineEdit _replaySeedInput;
	private ConfirmationDialog _floorDialog;
	private SpinBox _floorInput;
	private DebugStage _stage;
	private int _navigationId, _darkId, _revealMapId, _restartLevelId, _changeFloorId, _copyRunSeedId, _replayRunId;

	/// <summary>
	/// Shows the world's clearance grid for skeleton-sized bodies. Rebuilt on every enable, so it follows the current door
	/// states.
	/// </summary>
	public bool NavigationEnabled
	{
		get => IsInstanceValid(_navigationOverlay);
		set
		{
			if (IsInstanceValid(_navigationOverlay)) { _navigationOverlay.QueueFree(); }
			_navigationOverlay = value ? NavigationDebugOverlay.Build(Menu?.DebugWorld?.NavigationFor(NavigationLayer.Ground)) : null;
			if (_navigationOverlay != null) { Menu.DebugWorld.AddWorldNode(_navigationOverlay, Vector3.Zero); }
		}
	}

	/// <summary>The run seed of the world's floor.</summary>
	private ulong RunSeed => Menu?.DebugWorld?.RunSeed ?? RunState.DefaultSeed;

	protected override void BuildMenu(PopupMenu menu)
	{
		_navigationId = AddToggle(menu, "Navigation", () => NavigationEnabled, value => NavigationEnabled = value);
		_darkId = AddToggle(menu, "Dark level", () => Menu?.DebugWorld?.Lighting?.IsDark == true, SetDark);
		_stage = (Menu?.World as DebugArena)?.Stage;
		if (_stage != null)
		{
			PopupMenu walls = AddSubmenu(menu, "Walls", "WallsMenu");
			for (int index = 0; index < _stage.Arrangements.Length; index++)
			{
				int arrangement = index;
				AddRadio(walls, _stage.Arrangements[index].DisplayName, () => _stage.Arrangement == arrangement, () => SetWalls(arrangement));
			}
		}
		menu.AddSeparator();
		_revealMapId = AddAction(menu, "Reveal map", RevealMap);
		_restartLevelId = AddAction(menu, "Restart level", RestartLevel);
		_changeFloorId = AddAction(menu, "Change floor...", OpenFloorDialog);
		BuildFloorDialog();
		menu.AddSeparator();
		_copyRunSeedId = AddAction(menu, "Copy run seed", CopyRunSeed);
		_replayRunId = AddAction(menu, "Replay run...", OpenReplayDialog);
		BuildReplayDialog();
	}

	/// <summary>Disables what the world cannot do and shows the run seed.</summary>
	protected override void OnRefresh()
	{
		IDebugWorld world = Menu?.DebugWorld;
		SetDisabled(Popup, _navigationId, world == null);
		SetDisabled(Popup, _darkId, world?.Lighting == null);
		bool noLevel = Menu?.Level == null;
		foreach (int id in new[] { _revealMapId, _restartLevelId, _changeFloorId, _copyRunSeedId, _replayRunId })
		{
			SetDisabled(Popup, id, noLevel);
		}
		Popup.SetItemText(Popup.GetItemIndex(_copyRunSeedId), $"Copy run seed ({FormatSeed(RunSeed)})");
	}

	/// <summary>Switches an arena's stage to another wall arrangement; navigation, and its overlay when shown, follow.</summary>
	public void SetWalls(int arrangement)
	{
		if (_stage == null)
		{
			return;
		}
		_stage.SetArrangement(arrangement);
		if (NavigationEnabled)
		{
			NavigationEnabled = false;
			NavigationEnabled = true;
		}
	}

	/// <summary>Turns the world's lights off or back on; exploration is unchanged.</summary>
	public void SetDark(bool isDark)
	{
		if (Menu?.DebugWorld?.Lighting is { } lighting) { lighting.IsDark = isDark; }
	}

	/// <summary>Reveals the whole floor in the fog of war.</summary>
	public void RevealMap() => Menu?.Level?.Discovery?.RevealAll();

	/// <summary>Forgets this floor's saved changes and reloads the scene, regenerating the floor from its seed.</summary>
	public void RestartLevel()
	{
		GameSession.Instance?.ResetCurrentLevelDebugState();
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	/// <summary>Jumps directly to a floor using the session's normal hero and floor-state capture, without stair requirements.</summary>
	public bool ChangeFloor(uint depth) => Menu?.Level != null && GameSession.Instance?.ChangeDungeonDepthDebug(depth) == true;

	private void OpenFloorDialog()
	{
		if (Menu?.Level is not { } level) return;
		_floorInput.Value = level.Depth;
		_floorDialog.GetOkButton().Disabled = true;
		_floorDialog.PopupCentered();
		_floorInput.GetLineEdit().GrabFocus();
		_floorInput.GetLineEdit().SelectAll();
	}

	private void BuildFloorDialog()
	{
		_floorInput = new SpinBox { Name = "FloorNumber", MinValue = 1, MaxValue = uint.MaxValue, Step = 1, Rounded = true, UpdateOnTextChanged = true };
		var content = new VBoxContainer();
		content.AddChild(new Label { Text = "Floor number" });
		content.AddChild(_floorInput);
		content.AddChild(new Label { Text = "Keeps your hero, equipment and run seed." });
		_floorDialog = new ConfirmationDialog { Name = "ChangeFloorDialog", Title = "Change floor", OkButtonText = "Go" };
		_floorDialog.AddChild(content);
		_floorDialog.RegisterTextEnter(_floorInput.GetLineEdit());
		_floorInput.ValueChanged += value => _floorDialog.GetOkButton().Disabled = (uint)value == Menu?.Level?.Depth;
		_floorDialog.Confirmed += () =>
		{
			if (!ChangeFloor((uint)_floorInput.Value)) GD.PushWarning("Cannot change floors while the hero is acting, the run has ended or a scene transition is in progress.");
		};
		Menu.AddChild(_floorDialog);
	}

	/// <summary>Starts over from depth 1 with <paramref name="seed"/>; an active game replaces the save in its slot.</summary>
	public void ReplayRun(ulong seed) => GameSession.Instance?.ReplayRun(seed);

	private void CopyRunSeed()
	{
		DisplayServer.ClipboardSet(FormatSeed(RunSeed));
		GD.Print($"Copied run seed {FormatSeed(RunSeed)}.");
	}

	private void OpenReplayDialog()
	{
		_replaySeedInput.Text = FormatSeed(RunSeed);
		_replayDialog.PopupCentered();
		_replaySeedInput.GrabFocus();
		_replaySeedInput.SelectAll();
	}

	/// <summary>Seed entry for replaying a run; prefilled with the current seed, which can be replaced by any other.</summary>
	private void BuildReplayDialog()
	{
		_replaySeedInput = new LineEdit { PlaceholderText = "Run seed" };
		var content = new VBoxContainer();
		content.AddChild(new Label { Text = "Start over from depth 1 with this seed.\nAn active game replaces the save in its slot." });
		content.AddChild(_replaySeedInput);
		_replayDialog = new ConfirmationDialog { Title = "Replay run", OkButtonText = "Replay" };
		_replayDialog.AddChild(content);
		_replayDialog.RegisterTextEnter(_replaySeedInput);
		_replaySeedInput.TextChanged += text => _replayDialog.GetOkButton().Disabled = !TryParseSeed(text, out _);
		_replayDialog.Confirmed += () =>
		{
			if (TryParseSeed(_replaySeedInput.Text, out ulong seed)) { ReplayRun(seed); }
		};
		Menu.AddChild(_replayDialog);
	}

	private static string FormatSeed(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);

	private static bool TryParseSeed(string text, out ulong seed) =>
		ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seed);
}