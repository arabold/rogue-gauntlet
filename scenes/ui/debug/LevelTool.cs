using Godot;
using System;
using System.Globalization;

/// <summary>
/// Floor diagnostics and run controls: the navigation overlay, x-rays, darkness, an arena's walls, revealing the map,
/// restarting the floor and replaying the run from a seed. Revealing, restarting and the run seed need a generated
/// <see cref="Level"/>; darkness and navigation need a debug world.
/// </summary>
[GlobalClass]
public partial class LevelTool : DebugTool
{
	private NavigationDebugOverlay _navigationOverlay;
	private ConfirmationDialog _replayDialog;
	private LineEdit _replaySeedInput;
	private DebugStage _stage;
	private int _navigationId, _darkId, _revealMapId, _restartLevelId, _copyRunSeedId, _replayRunId;

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
			_navigationOverlay = value ? NavigationDebugOverlay.Build(Menu?.DebugWorld?.Navigation) : null;
			if (_navigationOverlay != null) { Menu.DebugWorld.AddWorldNode(_navigationOverlay, Vector3.Zero); }
		}
	}

	/// <summary>The run seed of the world's floor.</summary>
	private ulong RunSeed => Menu?.DebugWorld?.RunSeed ?? RunState.DefaultSeed;

	protected override void BuildMenu(PopupMenu menu)
	{
		_navigationId = AddToggle(menu, "Navigation", () => NavigationEnabled, value => NavigationEnabled = value);
		AddToggle(menu, "Door x-ray", () => GameDebug.DoorXrayEnabled, value => SetXray(() => GameDebug.DoorXrayEnabled = value));
		AddToggle(menu, "Monster x-ray", () => GameDebug.MonsterXrayEnabled, value => SetXray(() => GameDebug.MonsterXrayEnabled = value));
		AddToggle(menu, "Loot x-ray", () => GameDebug.LootXrayEnabled, value => SetXray(() => GameDebug.LootXrayEnabled = value));
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
		foreach (int id in new[] { _revealMapId, _restartLevelId, _copyRunSeedId, _replayRunId })
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

	/// <summary>Starts over from depth 1 with <paramref name="seed"/>; an active game replaces the save in its slot.</summary>
	public void ReplayRun(ulong seed) => GameSession.Instance?.ReplayRun(seed);

	/// <summary>Changes an x-ray switch and lets the silhouettes refresh.</summary>
	private static void SetXray(Action change)
	{
		change();
		SignalBus.EmitXraySettingsChanged();
	}

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
