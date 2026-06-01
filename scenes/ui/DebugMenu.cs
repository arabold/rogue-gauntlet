using Godot;
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

	[Export] public Godot.Collections.Array<Item> SpawnableItems { get; set; } = [];

	private Label _fpsLabel;
	private MenuButton _menuButton;
	private PopupMenu _debugMenu;
	private PopupMenu _spawnItemMenu;
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
		_debugMenu.AddItem("Reveal map", RevealMapId);
		_debugMenu.AddItem("Restart level", RestartLevelId);
		_debugMenu.AddSeparator();
		_debugMenu.AddItem("Kill player", KillPlayerId);
		_debugMenu.AddItem("Kill all enemies", KillEnemiesId);
		_debugMenu.AddSeparator();

		foreach (Item item in SpawnableItems)
		{
			if (item == null)
			{
				continue;
			}

			_spawnItemMenu.AddItem(item.Name, SpawnableItems.IndexOf(item));
		}

		_spawnItemMenu.IdPressed += OnSpawnItemPressed;
		_debugMenu.IdPressed += OnDebugMenuPressed;
		_debugMenu.AddChild(_spawnItemMenu);
		_debugMenu.AddSubmenuNodeItem("Spawn item", _spawnItemMenu);

		SetChecked(ToggleCombatLogsId, GameDebug.CombatLogsEnabled);
		SetChecked(ToggleAiLogsId, GameDebug.AiLogsEnabled);
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
			case RevealMapId:
				RevealMap();
				break;
			case RestartLevelId:
				RestartLevel();
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
		if (id < 0 || id >= SpawnableItems.Count || SpawnableItems[(int)id] == null)
		{
			return;
		}

		Player player = GetPlayer();
		Level level = GetLevel();
		if (player == null || level == null || player.LootableItemScene == null)
		{
			GD.PrintErr("Cannot spawn item without an active player, level, and lootable item scene.");
			return;
		}

		LootableItem lootableItem = player.LootableItemScene.Instantiate<LootableItem>();
		lootableItem.Item = SpawnableItems[(int)id];
		lootableItem.Quantity = 1;
		lootableItem.WaitForPlayerExited = true;

		Vector3 origin = player.GlobalPosition - player.GlobalTransform.Basis.Z.Normalized() * 2.0f;
		Vector3 spawnPosition = level.MapGenerator?.FindFreeSpawnPositionNear(origin) ?? origin;
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

		GetLevel()?.MapGenerator?.SetNavigationDebugVisible(enabled);

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

	private void RevealMap()
	{
		GetLevel()?.MapGenerator?.RevealAllFog();
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

	private Level GetLevel()
	{
		return this.GetAncestorOrNull<Level>() ?? GetTree().CurrentScene?.GetNodeOrNull<Level>("Level");
	}

	private void UpdateFpsLabel()
	{
		_fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}\n{GetRuntimeDebugSummary()}";
	}
}
