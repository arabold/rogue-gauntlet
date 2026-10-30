using Godot;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// In-game debug controls for fast scenario setup while running the main game scene.
/// </summary>
public partial class DebugMenu : HBoxContainer
{
	private const int ToggleDebugMeshesId = 1;
	private const int RevealMapId = 2;
	private const int RestartLevelId = 3;
	private const int KillPlayerId = 4;
	private const int KillEnemiesId = 5;
	private const int ToggleNavigationId = 6;
	private const int ToggleCombatLogsId = 7;
	private const int ToggleAiLogsId = 8;
	private const int ToggleDoorXrayId = 9;
	private const int ToggleMonsterXrayId = 10;
	private const int ToggleLootXrayId = 11;
	private const int ToggleDarkLevelId = 12;
	private const int CopyRunSeedId = 13;
	private const int ReplayRunId = 14;

	/// <summary>
	/// Category label to res:// folder the "Spawn item" submenu is built from. Every
	/// top-level <c>.tres</c> in a folder is offered for spawning, grouped under its label —
	/// add a folder here once and every item authored into it shows up automatically, instead
	/// of hand-listing every item (which silently drifts out of sync as new items are added).
	/// </summary>
	[Export]
	public Godot.Collections.Dictionary<string, string> SpawnableCategories { get; set; } = new()
	{
		{ "Weapons", "res://scenes/items/weapons/" },
		{ "Shields", "res://scenes/items/armor/" },
		{ "Potions", "res://scenes/items/potions/" },
		{ "Scrolls", "res://scenes/items/scrolls/" },
		{ "Jewelry", "res://scenes/items/jewelry/" },
		{ "Food", "res://scenes/items/food/food/" },
		{ "Drinks", "res://scenes/items/food/drink/" },
		{ "Gold", "res://scenes/items/gold/" },
		{ "Lamps and torches", "res://scenes/items/tools/lamp/" },
	};

	/// <summary>
	/// Creation policy (quality ladder and curse penalties) for the "Spawn quality" picker. Without it the picker is
	/// unavailable and equipment spawns as authored: Normal at scale 1. Affixes come from each definition's roll table.
	/// </summary>
	[Export] public ItemCreationPolicy SpawnPolicy { get; set; }

	/// <summary>Gameplay floor controlled by this menu, explicitly wired by the containing scene.</summary>
	[Export] public Level ActiveLevel { get; set; }
	private NavigationDebugOverlay _navigationOverlay;

	private readonly List<Item> _spawnableItems = [];

	private Label _fpsLabel;
	private MenuButton _menuButton;
	private PopupMenu _debugMenu;
	private PopupMenu _spawnItemMenu;
	private PopupMenu _spawnQualityMenu;
	/// <summary>Quality stamped onto spawned equipment; null spawns it as authored.</summary>
	private ItemQuality? _spawnQuality;
	/// <summary>Whether spawned equipment is cursed, with its curse status still unknown.</summary>
	private bool _spawnCursed;
	private const int SpawnCursedId = 100;
	private ConfirmationDialog _replayDialog;
	private LineEdit _replaySeedInput;
	private bool _debugMeshesEnabled;
	private bool _navigationEnabled;
	private double _fpsUpdateSeconds;

	public override void _Ready()
	{
		Visible = OS.IsDebugBuild();
		if (!Visible)
		{
			return;
		}

		_fpsLabel = GetNode<Label>("%FpsLabel");
		_menuButton = GetNode<MenuButton>("%DebugMenuButton");
		_debugMenu = _menuButton.GetPopup();
		_spawnItemMenu = new PopupMenu { Name = "SpawnItemMenu" };
		_spawnQualityMenu = new PopupMenu { Name = "SpawnQualityMenu" };

		BuildMenu();
		SetDebugMeshesEnabled(false);
		SetNavigationEnabled(false);
		UpdateFpsLabel();
	}

	public override void _Process(double delta)
	{
		if (!Visible)
		{
			return;
		}

		_fpsUpdateSeconds += delta;
		if (_fpsUpdateSeconds >= 0.25)
		{
			_fpsUpdateSeconds = 0;
			UpdateFpsLabel();
		}
	}

	private void BuildMenu()
	{
		_debugMenu.Clear();
		_spawnItemMenu.Clear();

		_debugMenu.AddCheckItem("Debug meshes", ToggleDebugMeshesId);
		_debugMenu.AddCheckItem("Navigation", ToggleNavigationId);
		_debugMenu.AddCheckItem("Combat logs", ToggleCombatLogsId);
		_debugMenu.AddCheckItem("AI logs", ToggleAiLogsId);
		_debugMenu.AddCheckItem("Door x-ray", ToggleDoorXrayId);
		_debugMenu.AddCheckItem("Monster x-ray", ToggleMonsterXrayId);
		_debugMenu.AddCheckItem("Loot x-ray", ToggleLootXrayId);
		_debugMenu.AddCheckItem("Dark level", ToggleDarkLevelId);
		_debugMenu.AboutToPopup += RefreshDarkLevel;
		_debugMenu.AddItem("Reveal map", RevealMapId);
		_debugMenu.AddItem("Restart level", RestartLevelId);
		_debugMenu.AddSeparator();
		_debugMenu.AddItem("Copy run seed", CopyRunSeedId);
		_debugMenu.AddItem("Replay run...", ReplayRunId);
		_debugMenu.AboutToPopup += RefreshRunSeed;
		BuildReplayDialog();
		_debugMenu.AddSeparator();
		_debugMenu.AddItem("Kill player", KillPlayerId);
		_debugMenu.AddItem("Kill all enemies", KillEnemiesId);
		_debugMenu.AddSeparator();

		BuildSpawnItemMenu();

		_debugMenu.IdPressed += OnDebugMenuPressed;
		_debugMenu.AddChild(_spawnItemMenu);
		_debugMenu.AddSubmenuNodeItem("Spawn item", _spawnItemMenu);
		if (SpawnPolicy?.QualityLadder != null)
		{
			BuildSpawnQualityMenu();
			_debugMenu.AddChild(_spawnQualityMenu);
			_debugMenu.AddSubmenuNodeItem("Spawn quality", _spawnQualityMenu);
		}
		else if (SpawnPolicy != null)
		{
			GD.PushError("DebugMenu: SpawnPolicy needs a quality ladder.");
		}

		SetChecked(ToggleCombatLogsId, GameDebug.CombatLogsEnabled);
		SetChecked(ToggleAiLogsId, GameDebug.AiLogsEnabled);
		SetChecked(ToggleDoorXrayId, GameDebug.DoorXrayEnabled);
		SetChecked(ToggleMonsterXrayId, GameDebug.MonsterXrayEnabled);
		SetChecked(ToggleLootXrayId, GameDebug.LootXrayEnabled);
	}

	/// <summary>
	/// Builds one submenu per <see cref="SpawnableCategories"/> entry, populated by scanning
	/// its folder. IDs are indices into <see cref="_spawnableItems"/>, shared across every
	/// category submenu so a single <see cref="OnSpawnItemPressed"/> handler covers all of them.
	/// </summary>
	private void BuildSpawnItemMenu()
	{
		_spawnableItems.Clear();

		foreach (var (label, directory) in SpawnableCategories)
		{
			var entries = LoadItemsInDirectory(directory)
				.OrderBy(e => e.Item.Name, System.StringComparer.Ordinal)
				.ThenBy(e => (e.Item as EquipableItem)?.Tier ?? 0)
				.ThenBy(e => e.Path, System.StringComparer.Ordinal)
				.ToList();
			if (entries.Count == 0)
			{
				continue;
			}

			var categoryMenu = new PopupMenu { Name = $"Spawn{label}Menu" };
			string[] labels = DescribeEntries(entries);
			for (int i = 0; i < entries.Count; i++)
			{
				_spawnableItems.Add(entries[i].Item);
				categoryMenu.AddItem(labels[i], _spawnableItems.Count - 1);
				categoryMenu.SetItemTooltip(i, entries[i].Path);
			}

			categoryMenu.IdPressed += OnSpawnItemPressed;
			_spawnItemMenu.AddChild(categoryMenu);
			_spawnItemMenu.AddSubmenuNodeItem(label, categoryMenu);
		}
	}

	/// <summary>
	/// Radio list of spawn qualities plus a "Cursed" toggle. Id 0 spawns equipment as authored; quality ids are their
	/// <see cref="ItemQuality"/> plus one.
	/// </summary>
	private void BuildSpawnQualityMenu()
	{
		_spawnQualityMenu.Clear();
		_spawnQualityMenu.AddRadioCheckItem("Authored", 0);
		foreach (ItemQuality quality in System.Enum.GetValues<ItemQuality>())
		{
			_spawnQualityMenu.AddRadioCheckItem(quality.ToString(), (int)quality + 1);
		}

		_spawnQualityMenu.SetItemChecked(0, true);
		_spawnQualityMenu.AddSeparator();
		_spawnQualityMenu.AddCheckItem("Cursed", SpawnCursedId);
		_spawnQualityMenu.IdPressed += OnSpawnQualityPressed;
	}

	private void OnSpawnQualityPressed(long id)
	{
		if (id == SpawnCursedId)
		{
			_spawnCursed = !_spawnCursed;
			_spawnQualityMenu.SetItemChecked(_spawnQualityMenu.GetItemIndex(SpawnCursedId), _spawnCursed);
			return;
		}

		_spawnQuality = id == 0 ? null : (ItemQuality)(id - 1);
		for (int i = 0; i < _spawnQualityMenu.ItemCount; i++)
		{
			if (_spawnQualityMenu.IsItemRadioCheckable(i))
			{
				_spawnQualityMenu.SetItemChecked(i, _spawnQualityMenu.GetItemId(i) == id);
			}
		}
	}

	/// <summary>
	/// The picked definition itself, or for equipment with a picked quality or the cursed toggle a copy rolled like a drop
	/// at the current depth with that quality (Normal when only cursed) and curse forced.
	/// </summary>
	private Item CreateSpawnItem(Item definition)
	{
		if (_spawnQuality == null && !_spawnCursed || definition is not EquipableItem { RollTable: not null })
		{
			return definition;
		}

		var item = (EquipableItem)ItemFactory.Create(definition);
		var rng = new RandomNumberGenerator();
		rng.Randomize();
		LootRoller.ApplyRoll(item, ActiveLevel?.Depth ?? 1, rng, SpawnPolicy,
			_spawnQuality ?? ItemQuality.Normal, _spawnCursed);
		return item;
	}

	/// <summary>
	/// Display names repeat across tiers and model variants, so equipment shows its tier and any
	/// remaining clash adds the resource name. The full path is in each entry's tooltip.
	/// </summary>
	private static string[] DescribeEntries(List<(Item Item, string Path)> entries)
	{
		static string Describe(Item item) => item is EquipableItem equipable ? $"{item.Name} · T{equipable.Tier}" : item.Name;
		var collisions = entries.GroupBy(e => Describe(e.Item)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
		return entries.Select(e => collisions.Contains(Describe(e.Item))
			? $"{Describe(e.Item)} · {e.Path.GetFile().GetBaseName()}"
			: Describe(e.Item)).ToArray();
	}

	/// <summary>
	/// Recursively loads every <c>.tres</c> in a folder as an <see cref="Item"/> with its path.
	/// Entries that fail to load or aren't an <see cref="Item"/> are skipped.
	/// </summary>
	private static List<(Item Item, string Path)> LoadItemsInDirectory(string directory)
	{
		var items = new List<(Item Item, string Path)>();
		DirAccess dir = DirAccess.Open(directory);
		if (dir == null)
		{
			return items;
		}

		dir.ListDirBegin();
		for (string fileName = dir.GetNext(); fileName != ""; fileName = dir.GetNext())
		{
			if (dir.CurrentIsDir())
			{
				if (!fileName.StartsWith(".")) { items.AddRange(LoadItemsInDirectory(directory.PathJoin(fileName))); }
				continue;
			}
			if (!fileName.EndsWith(".tres")) { continue; }

			string path = directory.PathJoin(fileName);
			Item item = ResourceLoader.Load<Item>(path);
			if (item != null)
			{
				items.Add((item, path));
			}
		}

		dir.ListDirEnd();
		return items;
	}

	private void OnDebugMenuPressed(long id)
	{
		switch (id)
		{
			case ToggleDebugMeshesId:
				SetDebugMeshesEnabled(!_debugMeshesEnabled);
				break;
			case ToggleNavigationId:
				SetNavigationEnabled(!_navigationEnabled);
				break;
			case ToggleCombatLogsId:
				SetCombatLogsEnabled(!GameDebug.CombatLogsEnabled);
				break;
			case ToggleAiLogsId:
				SetAiLogsEnabled(!GameDebug.AiLogsEnabled);
				break;
			case ToggleDoorXrayId:
				GameDebug.DoorXrayEnabled = !GameDebug.DoorXrayEnabled;
				SetChecked(ToggleDoorXrayId, GameDebug.DoorXrayEnabled);
				SignalBus.EmitXraySettingsChanged();
				break;
			case ToggleMonsterXrayId:
				GameDebug.MonsterXrayEnabled = !GameDebug.MonsterXrayEnabled;
				SetChecked(ToggleMonsterXrayId, GameDebug.MonsterXrayEnabled);
				SignalBus.EmitXraySettingsChanged();
				break;
			case ToggleLootXrayId:
				GameDebug.LootXrayEnabled = !GameDebug.LootXrayEnabled;
				SetChecked(ToggleLootXrayId, GameDebug.LootXrayEnabled);
				SignalBus.EmitXraySettingsChanged();
				break;
			case ToggleDarkLevelId:
				var level = ActiveLevel;
				level?.SetDark(!level.IsDark);
				RefreshDarkLevel();
				break;
			case RevealMapId:
				RevealMap();
				break;
			case RestartLevelId:
				RestartLevel();
				break;
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
			case KillPlayerId:
				KillPlayer();
				break;
			case KillEnemiesId:
				KillEnemies();
				break;
		}
	}

	private void OnSpawnItemPressed(long id)
	{
		if (id < 0 || id >= _spawnableItems.Count || _spawnableItems[(int)id] == null)
		{
			return;
		}

		Player player = GetPlayer();
		Level level = ActiveLevel;
		if (player == null || level == null || player.LootableItemScene == null)
		{
			GD.PrintErr("Cannot spawn item without an active player, level, and lootable item scene.");
			return;
		}

		LootableItem lootableItem = player.LootableItemScene.Instantiate<LootableItem>();
		lootableItem.Item = CreateSpawnItem(_spawnableItems[(int)id]);
		lootableItem.Quantity = 1;
		lootableItem.WaitForPlayerExited = true;

		Vector3 origin = player.GlobalPosition - player.GlobalTransform.Basis.Z.Normalized() * 2.0f;
		Vector3 spawnPosition = level.Floor?.FindFreePositionNear(origin) ?? origin;
		level.AddWorldNode(lootableItem, spawnPosition);
	}

	private void SetDebugMeshesEnabled(bool enabled)
	{
		_debugMeshesEnabled = enabled;
		AttackController.GlobalDebugDrawEnabled = enabled;

		foreach (Node node in GetTree().GetNodesInGroup("attack_controller"))
		{
			if (node is AttackController attackController)
			{
				attackController.DebugDrawEnabled = enabled;
			}
		}

		foreach (Node node in GetTree().GetNodesInGroup("debug_mesh"))
		{
			if (node is Node3D node3D)
			{
				node3D.Visible = enabled;
			}
		}

		SetChecked(ToggleDebugMeshesId, enabled);
	}

	/// <summary>
	/// Toggles navigation debug visuals: the baked navmesh overlay plus each enemy's red
	/// <see cref="NavigationAgent3D"/> path. Agent paths are driven per-agent (their server-level
	/// flag isn't honored at runtime here), so this applies to every agent currently in the scene.
	/// </summary>
	private void SetNavigationEnabled(bool enabled)
	{
		_navigationEnabled = enabled;

		foreach (Node node in GetTree().GetNodesInGroup("navigation_agent"))
		{
			if (node is NavigationAgent3D agent)
			{
				agent.DebugEnabled = enabled;
			}
		}

		// Rebuilt on every enable, so the overlay follows the current bake and door states.
		if (IsInstanceValid(_navigationOverlay))
		{
			_navigationOverlay.QueueFree();
		}
		_navigationOverlay = enabled ? NavigationDebugOverlay.Build(ActiveLevel?.MapGenerator.NavigationRegion) : null;
		if (_navigationOverlay != null)
		{
			ActiveLevel.AddWorldNode(_navigationOverlay);
		}

		SetChecked(ToggleNavigationId, enabled);
	}

	private void SetCombatLogsEnabled(bool enabled)
	{
		GameDebug.CombatLogsEnabled = enabled;
		SetChecked(ToggleCombatLogsId, enabled);
	}

	private void SetAiLogsEnabled(bool enabled)
	{
		GameDebug.AiLogsEnabled = enabled;
		SetChecked(ToggleAiLogsId, enabled);
	}

	private void SetChecked(int itemId, bool enabled)
	{
		int itemIndex = _debugMenu.GetItemIndex(itemId);
		if (itemIndex >= 0)
		{
			_debugMenu.SetItemChecked(itemIndex, enabled);
		}
	}

	/// <summary>Read the active level on opening so the checkbox follows level changes and external overrides.</summary>
	private void RefreshDarkLevel()
	{
		var level = ActiveLevel;
		_debugMenu.SetItemDisabled(_debugMenu.GetItemIndex(ToggleDarkLevelId), level == null);
		SetChecked(ToggleDarkLevelId, level?.IsDark == true);
	}

	/// <summary>The run this floor belongs to; gameplay reads it from the level, not the session.</summary>
	private ulong RunSeed => ActiveLevel?.Run?.RunSeed ?? GameSession.Instance?.Run.RunSeed ?? RunState.DefaultSeed;

	private static string FormatSeed(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);

	private void RefreshRunSeed() =>
		_debugMenu.SetItemText(_debugMenu.GetItemIndex(CopyRunSeedId), $"Copy run seed ({FormatSeed(RunSeed)})");

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
			if (TryParseSeed(_replaySeedInput.Text, out ulong seed)) GameSession.Instance?.ReplayRun(seed);
		};
		AddChild(_replayDialog);
	}

	private static bool TryParseSeed(string text, out ulong seed) =>
		ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seed);

	private void RevealMap()
	{
		ActiveLevel?.Discovery?.RevealAll();
	}

	private void RestartLevel()
	{
		GameSession.Instance?.ResetCurrentLevelDebugState();
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	private void KillPlayer()
	{
		Player player = GetPlayer();
		if (player == null || player.HealthComponent == null || player.HealthComponent.IsDead)
		{
			return;
		}

		player.HealthComponent.TakeDamage(player.HealthComponent.CurrentHealth);
	}

	private void KillEnemies()
	{
		foreach (Node enemy in GetTree().GetNodesInGroup("enemy"))
		{
			HealthComponent health = enemy.GetNodeOrNull<HealthComponent>("HealthComponent");
			if (health != null && !health.IsDead)
			{
				health.TakeDamage(health.CurrentHealth);
			}
		}
	}

	private Player GetPlayer()
	{
		return GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
	}

	private string GetRuntimeDebugSummary()
	{
		int enemies = GetTree().GetNodesInGroup("enemy").Count;
		int nodes = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		int drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		return $"Enemies: {enemies}  Nodes: {nodes}  Draw: {drawCalls}\n{ScenePool.GetDebugSummary()}";
	}

	private void UpdateFpsLabel()
	{
		_fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}\n{GetRuntimeDebugSummary()}";
	}
}