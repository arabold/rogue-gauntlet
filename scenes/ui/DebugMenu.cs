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
	/// <summary>How far a debug-spawned item is thrown: the default monster drop distance.</summary>
	private const float DebugDropDistance = 1.5f;
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
	/// <c>.tres</c> in a folder or its subfolders is offered for spawning, grouped under its label —
	/// add a folder here once and every item authored into it shows up automatically, instead
	/// of hand-listing every item (which silently drifts out of sync as new items are added).
	/// Spell weapons are the exception: they get the <see cref="SpellWeaponCategories"/> submenus.
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
	/// Spell weapons are authored among the other weapons but listed in their own submenus, right
	/// after the folder they are found in: one-handed wands apart from the stronger two-handed staves.
	/// </summary>
	private static readonly (string Label, System.Func<Item, bool> Matches)[] SpellWeaponCategories =
	[
		("Wands", item => item is MagicStaff { IsTwoHanded: false }),
		("Staves", item => item is MagicStaff { IsTwoHanded: true }),
	];

	/// <summary>
	/// Creation policy (quality ladder and curse penalties) for the "Spawn quality" picker. Without it the picker is
	/// unavailable and equipment spawns exactly as saved. Affixes come from each definition's roll table.
	/// </summary>
	[Export] public ItemCreationPolicy SpawnPolicy { get; set; }

	/// <summary>Spells the "Cast spell" submenu casts straight from the player; without it the submenu is unavailable.</summary>
	[Export] public SpellPool CastSpells { get; set; }

	/// <summary>Gameplay floor controlled by this menu, explicitly wired by the containing scene.</summary>
	[Export] public Level ActiveLevel { get; set; }
	private NavigationDebugOverlay _navigationOverlay;

	/// <summary>What each "Spawn item" entry creates: a definition and, for a wand or staff, the spell it carries.</summary>
	private readonly List<(Item Definition, SpellDefinition Spell)> _spawnables = [];
	/// <summary>What each "Cast spell" entry casts.</summary>
	private readonly List<(SpellDefinition Spell, int Level)> _castables = [];

	private Label _fpsLabel;
	private MenuButton _menuButton;
	private PopupMenu _debugMenu;
	private PopupMenu _spawnItemMenu;
	private PopupMenu _spawnQualityMenu;
	private PopupMenu _castSpellMenu;
	/// <summary>Quality forced onto spawned equipment; null rolls it like a drop at the current depth.</summary>
	private ItemQuality? _spawnQuality;
	/// <summary>Whether spawned equipment is cursed, with its curse status still unknown.</summary>
	private bool _spawnCursed;
	private const int SpawnCursedId = 100;
	private const int SpawnRandomQualityId = 101;
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
		_castSpellMenu = new PopupMenu { Name = "CastSpellMenu" };

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
		if (CastSpells != null)
		{
			BuildCastSpellMenu();
			_debugMenu.AddSeparator();
			_debugMenu.AddChild(_castSpellMenu);
			_debugMenu.AddSubmenuNodeItem("Cast spell", _castSpellMenu);
		}

		SetChecked(ToggleCombatLogsId, GameDebug.CombatLogsEnabled);
		SetChecked(ToggleAiLogsId, GameDebug.AiLogsEnabled);
		SetChecked(ToggleDoorXrayId, GameDebug.DoorXrayEnabled);
		SetChecked(ToggleMonsterXrayId, GameDebug.MonsterXrayEnabled);
		SetChecked(ToggleLootXrayId, GameDebug.LootXrayEnabled);
	}

	/// <summary>
	/// Builds one submenu per <see cref="SpawnableCategories"/> entry, populated by scanning
	/// its folder, plus the <see cref="SpellWeaponCategories"/> split out of them. IDs are indices
	/// into <see cref="_spawnables"/>, shared across every category submenu so a single
	/// <see cref="OnSpawnItemPressed"/> handler covers all of them.
	/// </summary>
	private void BuildSpawnItemMenu()
	{
		_spawnables.Clear();

		var categories = new List<(string Label, IEnumerable<(Item Item, string Path)> Entries)>();
		foreach (var (label, directory) in SpawnableCategories)
		{
			var entries = LoadItemsInDirectory(directory);
			categories.Add((label, entries.Where(e => e.Item is not MagicStaff)));
			categories.AddRange(SpellWeaponCategories.Select(c => (c.Label, entries.Where(e => c.Matches(e.Item)))));
		}

		// Merges spell weapons found in several folders into one submenu, placed where they first appear.
		foreach (var category in categories.GroupBy(c => c.Label, c => c.Entries))
		{
			AddSpawnCategory(category.Key, category.SelectMany(entries => entries));
		}
	}

	/// <summary>
	/// Adds one "Spawn item" submenu listing <paramref name="entries"/> by name and tier; an empty one is left out. A
	/// wand or staff opens its own spell submenu instead of spawning directly.
	/// </summary>
	private void AddSpawnCategory(string label, IEnumerable<(Item Item, string Path)> entries)
	{
		var sorted = entries
			.OrderBy(e => e.Item.Name, System.StringComparer.Ordinal)
			.ThenBy(e => (e.Item as EquipableItem)?.Tier ?? 0)
			.ThenBy(e => e.Path, System.StringComparer.Ordinal)
			.ToList();
		if (sorted.Count == 0)
		{
			return;
		}

		var categoryMenu = new PopupMenu { Name = $"Spawn{label}Menu" };
		string[] labels = DescribeEntries(sorted);
		for (int i = 0; i < sorted.Count; i++)
		{
			var (item, path) = sorted[i];
			if (item is MagicStaff { SpellPool: not null } staff)
			{
				PopupMenu spellMenu = BuildSpellWeaponMenu(staff);
				categoryMenu.AddChild(spellMenu);
				categoryMenu.AddSubmenuNodeItem(labels[i], spellMenu);
			}
			else
			{
				categoryMenu.AddItem(labels[i], AddSpawnable(item, null));
			}
			categoryMenu.SetItemTooltip(i, path);
		}

		categoryMenu.IdPressed += OnSpawnItemPressed;
		_spawnItemMenu.AddChild(categoryMenu);
		_spawnItemMenu.AddSubmenuNodeItem(label, categoryMenu);
	}

	/// <summary>
	/// The spells <paramref name="staff"/> can roll: each entry spawns the weapon carrying that spell, so the debug menu
	/// offers exactly the variants a dungeon drop can be.
	/// </summary>
	private PopupMenu BuildSpellWeaponMenu(MagicStaff staff)
	{
		var menu = new PopupMenu();
		foreach (SpellDefinition spell in staff.SpellPool.Spells.Where(staff.CanBind))
		{
			menu.AddItem(spell.DisplayName, AddSpawnable(staff, spell));
		}

		menu.IdPressed += OnSpawnItemPressed;
		return menu;
	}

	/// <summary>Registers what a "Spawn item" entry creates and returns its menu id.</summary>
	private int AddSpawnable(Item definition, SpellDefinition spell)
	{
		_spawnables.Add((definition, spell));
		return _spawnables.Count - 1;
	}

	/// <summary>
	/// Radio list of "Random", which rolls the quality like a drop at the current depth, and every quality (ids are their
	/// <see cref="ItemQuality"/>), plus a "Cursed" toggle.
	/// </summary>
	private void BuildSpawnQualityMenu()
	{
		_spawnQualityMenu.Clear();
		_spawnQualityMenu.AddRadioCheckItem("Random", SpawnRandomQualityId);
		foreach (ItemQuality quality in System.Enum.GetValues<ItemQuality>())
		{
			_spawnQualityMenu.AddRadioCheckItem(quality.ToString(), (int)quality);
		}

		CheckRadio(_spawnQualityMenu, SpawnRandomQualityId);
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

		_spawnQuality = id == SpawnRandomQualityId ? null : (ItemQuality)id;
		CheckRadio(_spawnQualityMenu, id);
	}

	/// <summary>Checks the radio item <paramref name="id"/> of <paramref name="menu"/> and unchecks its other radio items.</summary>
	private static void CheckRadio(PopupMenu menu, long id)
	{
		for (int i = 0; i < menu.ItemCount; i++)
		{
			if (menu.IsItemRadioCheckable(i))
			{
				menu.SetItemChecked(i, menu.GetItemId(i) == id);
			}
		}
	}

	/// <summary>One submenu per spell in <see cref="CastSpells"/>, listing every level it can be cast at.</summary>
	private void BuildCastSpellMenu()
	{
		_castables.Clear();
		foreach (SpellDefinition spell in CastSpells.Spells.Where(spell => spell != null))
		{
			var levelMenu = new PopupMenu();
			for (int level = 1; level <= spell.LevelCount; level++)
			{
				if (!spell.IsValidLevel(level)) { continue; }
				_castables.Add((spell, level));
				levelMenu.AddItem($"Level {level}", _castables.Count - 1);
			}

			levelMenu.IdPressed += OnCastSpellPressed;
			_castSpellMenu.AddChild(levelMenu);
			_castSpellMenu.AddSubmenuNodeItem(spell.DisplayName, levelMenu);
		}
	}

	/// <summary>
	/// Casts the picked spell from the player at once, without mana, cooldown or wind-up, at the picked level and with the
	/// equipped weapon's hit (unarmed without one), the way a wand of that tier casts it at release.
	/// </summary>
	private void OnCastSpellPressed(long id)
	{
		if (id < 0 || id >= _castables.Count)
		{
			return;
		}

		Player player = GetPlayer();
		if (player?.SpellCaster == null || player.IsDead)
		{
			GD.PrintErr("Cannot cast a spell without a living player.");
			return;
		}

		var (spell, level) = _castables[(int)id];
		var power = new SpellPower(level, player.Stats.ResolveAttack(player.Inventory?.EquippedWeapon));
		var context = new SpellCastContext(player, null, SpellTargetIntent.Hostile, power);
		if (!player.SpellCaster.CastImmediate(spell, this, in context))
		{
			GD.Print($"{spell.DisplayName} (Level {level}) could not be cast here.");
		}
	}

	/// <summary>
	/// The picked definition itself, or a copy: equipment is rolled like a drop at the current depth with the picked
	/// quality (rolled too when "Random") and the curse toggle forced, and a wand or staff carries
	/// <paramref name="spell"/>, which the roll would otherwise redraw from its pool.
	/// </summary>
	private Item CreateSpawnItem(Item definition, SpellDefinition spell)
	{
		bool roll = SpawnPolicy?.QualityLadder != null && definition is EquipableItem { RollTable: not null };
		if (!roll && spell == null)
		{
			return definition;
		}

		var item = (EquipableItem)ItemFactory.Create(definition);
		if (roll)
		{
			var rng = new RandomNumberGenerator();
			rng.Randomize();
			LootRoller.ApplyRoll(item, ActiveLevel?.Depth ?? 1, rng, SpawnPolicy, _spawnQuality, _spawnCursed);
		}
		if (item is MagicStaff staff && spell != null)
		{
			staff.BoundSpell = spell;
		}
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
		if (id < 0 || id >= _spawnables.Count)
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
		var (definition, spell) = _spawnables[(int)id];
		lootableItem.Item = CreateSpawnItem(definition, spell);
		lootableItem.Quantity = 1;

		// Thrown out of a free spot ahead of the player like a monster drop, then collected by the magnet.
		Vector3 origin = player.GlobalPosition - player.GlobalTransform.Basis.Z.Normalized() * 2.0f;
		Vector3 source = level.Floor?.FindFreePositionNear(origin) ?? origin;
		lootableItem.ThrowFrom(source);
		Vector3 direction = Vector3.Back.Rotated(Vector3.Up, GD.Randf() * Mathf.Tau);
		level.AddWorldNode(lootableItem, level.Floor?.FindDropLanding(source, direction, DebugDropDistance) ?? source);
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

	/// <summary>Toggles the navigation debug overlay: the floor's clearance grid for skeleton-sized bodies.</summary>
	private void SetNavigationEnabled(bool enabled)
	{
		_navigationEnabled = enabled;

		// Rebuilt on every enable, so the overlay follows the current door states.
		if (IsInstanceValid(_navigationOverlay))
		{
			_navigationOverlay.QueueFree();
		}
		_navigationOverlay = enabled ? NavigationDebugOverlay.Build(ActiveLevel?.Navigation) : null;
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