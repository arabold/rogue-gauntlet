using Godot;
using System.Globalization;

/// <summary>
/// Floor diagnostics and run controls: the navigation overlay, x-rays, darkness, revealing the map, restarting the floor
/// and replaying the run from a seed. Revealing the map needs a generated <see cref="Level"/>; darkness and navigation
/// need a debug world.
/// </summary>
[GlobalClass]
public partial class LevelTool : DebugTool
{
	private const int NavigationId = 0;
	private const int DoorXrayId = 1;
	private const int MonsterXrayId = 2;
	private const int LootXrayId = 3;
	private const int DarkId = 4;
	private const int RevealMapId = 5;
	private const int RestartLevelId = 6;
	private const int CopyRunSeedId = 7;
	private const int ReplayRunId = 8;

	private NavigationDebugOverlay _navigationOverlay;
	private ConfirmationDialog _replayDialog;
	private LineEdit _replaySeedInput;

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

	public bool DoorXrayEnabled
	{
		get => GameDebug.DoorXrayEnabled;
		set { GameDebug.DoorXrayEnabled = value; SignalBus.EmitXraySettingsChanged(); }
	}

	public bool MonsterXrayEnabled
	{
		get => GameDebug.MonsterXrayEnabled;
		set { GameDebug.MonsterXrayEnabled = value; SignalBus.EmitXraySettingsChanged(); }
	}

	public bool LootXrayEnabled
	{
		get => GameDebug.LootXrayEnabled;
		set { GameDebug.LootXrayEnabled = value; SignalBus.EmitXraySettingsChanged(); }
	}

	/// <summary>The run this floor belongs to; gameplay reads it from the level, not the session.</summary>
	public ulong RunSeed => Menu?.Level?.Run?.RunSeed ?? GameSession.Instance?.Run.RunSeed ?? RunState.DefaultSeed;

	public override void BuildMenu(PopupMenu menu)
	{
		menu.AddCheckItem("Navigation", NavigationId);
		menu.AddCheckItem("Door x-ray", DoorXrayId);
		menu.AddCheckItem("Monster x-ray", MonsterXrayId);
		menu.AddCheckItem("Loot x-ray", LootXrayId);
		menu.AddCheckItem("Dark level", DarkId);
		menu.AddSeparator();
		menu.AddItem("Reveal map", RevealMapId);
		menu.AddItem("Restart level", RestartLevelId);
		menu.AddSeparator();
		menu.AddItem("Copy run seed", CopyRunSeedId);
		menu.AddItem("Replay run...", ReplayRunId);
		menu.IdPressed += OnPressed;
		BuildReplayDialog();
		RefreshMenu(menu);
	}

	/// <summary>Reads the world on opening, so the checks follow level changes and changes made by code.</summary>
	public override void RefreshMenu(PopupMenu menu)
	{
		IDebugWorld world = Menu?.DebugWorld;
		SetChecked(menu, NavigationId, NavigationEnabled);
		SetChecked(menu, DoorXrayId, DoorXrayEnabled);
		SetChecked(menu, MonsterXrayId, MonsterXrayEnabled);
		SetChecked(menu, LootXrayId, LootXrayEnabled);
		SetChecked(menu, DarkId, world?.IsDark == true);
		SetDisabled(menu, NavigationId, world == null);
		SetDisabled(menu, DarkId, world == null);
		SetDisabled(menu, RevealMapId, Menu?.Level == null);
		menu.SetItemText(menu.GetItemIndex(CopyRunSeedId), $"Copy run seed ({FormatSeed(RunSeed)})");
	}

	/// <summary>Turns the world's lights off or back on; exploration is unchanged.</summary>
	public void SetDark(bool isDark) => Menu?.DebugWorld?.SetDark(isDark);

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

	private void OnPressed(long id)
	{
		switch (id)
		{
			case NavigationId: NavigationEnabled = !NavigationEnabled; break;
			case DoorXrayId: DoorXrayEnabled = !DoorXrayEnabled; break;
			case MonsterXrayId: MonsterXrayEnabled = !MonsterXrayEnabled; break;
			case LootXrayId: LootXrayEnabled = !LootXrayEnabled; break;
			case DarkId: SetDark(Menu?.DebugWorld?.IsDark != true); break;
			case RevealMapId: RevealMap(); break;
			case RestartLevelId: RestartLevel(); break;
			case CopyRunSeedId:
				DisplayServer.ClipboardSet(FormatSeed(RunSeed));
				GD.Print($"Copied run seed {FormatSeed(RunSeed)}.");
				break;
			case ReplayRunId:
				_replaySeedInput.Text = FormatSeed(RunSeed);
				_replayDialog.PopupCentered();
				_replaySeedInput.GrabFocus();
				_replaySeedInput.SelectAll();
				break;
		}
		RefreshMenu(Popup);
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
