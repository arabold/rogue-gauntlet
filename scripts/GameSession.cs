using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Coordinates game flow between menus, gameplay, and save slots.
/// </summary>
public partial class GameSession : Node
{
	public static GameSession Instance { get; private set; }

	public const string MenuScenePath = "res://scenes/menu/main_menu.tscn";
	public const string GameplayScenePath = "res://scenes/main/main.tscn";

	public int ActiveSlotId { get; private set; }
	public ulong ActiveSeed { get; private set; } = 42;
	public uint ActiveDungeonDepth { get; private set; } = 1;
	public bool IsGameActive => ActiveSlotId > 0;

	private readonly IdentificationService _identification = new();
	private bool _identificationReady;

	/// <summary>
	/// Per-run hidden-identity state for potions, scrolls, and similar items. Starting or loading a run initializes
	/// it; anything that plays without one (a scene run directly, or back at the menu) gets the default run's
	/// disguises from <see cref="ActiveSeed"/> on first use, so the catalog never loads at boot.
	/// </summary>
	public IdentificationService Identification
	{
		get
		{
			if (!_identificationReady)
			{
				_identification.Initialize(ActiveSeed);
				_identificationReady = true;
			}

			return _identification;
		}
	}

	/// <summary>Id of the run's character class; empty when no game is active.</summary>
	public string ActiveCharacterClassId { get; private set; } = "";

	/// <summary>
	/// The run's character class, resolved from the catalog at game start. Null when no
	/// game is active (e.g. running main.tscn directly), in which case the player keeps
	/// its authored appearance and stats.
	/// </summary>
	public CharacterClass ActiveCharacterClass { get; private set; }

	private CharacterCatalog _characterCatalog;

	public enum LevelTravelDirection
	{
		Up = 1,
		Down = 2
	}

	public LevelTravelDirection? PendingTravelDirection { get; private set; }
	private double _stairArrivalTicks;
	private const double StairArrivalLockSeconds = 1.0;
	private const double LevelFadeOutSeconds = 0.25;
	private const double LevelFadeInSeconds = 0.45;
	private const int LevelFadeLayer = 128;
	private static readonly Color TransparentBlack = new(0f, 0f, 0f, 0f);

	private SaveGame _activeSave;
	private SaveGame _pendingLoadedSave;
	private double _sessionStartedAtMsec;
	private bool _isSceneTransitioning;
	private bool _saveAfterNextSpawn;
	private uint _lootRollsConsumed;
	/// <summary>Read-only continuation cursor for gameplay-time randomness and diagnostics.</summary>
	public uint LootRollsConsumed => _lootRollsConsumed;
	private CanvasLayer _levelFadeLayer;
	private ColorRect _levelFadeRect;
	private Tween _levelFadeTween;

	public override void _Ready()
	{
		if (Instance != null && GodotObject.IsInstanceValid(Instance))
		{
			GD.PrintErr("Multiple GameSession instances detected.");
			QueueFree();
			return;
		}

		Instance = this;
		ProcessMode = ProcessModeEnum.Always;

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += OnPlayerSpawned,
			signalBus => signalBus.PlayerSpawned -= OnPlayerSpawned);

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.ItemConsumed += OnItemConsumed,
			signalBus => signalBus.ItemConsumed -= OnItemConsumed);

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.ItemEquipped += OnItemEquipped,
			signalBus => signalBus.ItemEquipped -= OnItemEquipped);
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		base._ExitTree();
	}

	public void StartNewGame(int slotId, string characterClassId = null)
	{
		if (_isSceneTransitioning)
		{
			return;
		}

		SaveService.GetSlotPath(slotId);
		ActiveSlotId = slotId;
		ActiveSeed = (ulong)DateTime.UtcNow.Ticks;
		ActiveDungeonDepth = 1;
		PendingTravelDirection = LevelTravelDirection.Down;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		_pendingLoadedSave = null;
		_saveAfterNextSpawn = true;
		_lootRollsConsumed = 0;
		_identification.Initialize(ActiveSeed);
		_identificationReady = true;
		ResolveCharacterClass(characterClassId);

		// The hero knows their own supplies: class starting consumables are never disguised.
		foreach (string typeId in ActiveCharacterClass?.PreIdentifiedTypeIds ?? Array.Empty<string>())
		{
			Identification.Identify(typeId);
		}

		string now = DateTime.UtcNow.ToString("O");
		_activeSave = new SaveGame
		{
			SlotId = slotId,
			RunId = Guid.NewGuid().ToString("N"),
			CreatedAtUtc = now,
			SavedAtUtc = now,
			CharacterClassId = ActiveCharacterClassId,
			Seed = ActiveSeed,
			DungeonDepth = ActiveDungeonDepth,
		};

		GetTree().Paused = false;
		_isSceneTransitioning = true;
		ShowBlackScreen();
		GetTree().ChangeSceneToFile(GameplayScenePath);
	}

	public void LoadGame(int slotId)
	{
		if (_isSceneTransitioning)
		{
			return;
		}

		SaveGame saveGame = SaveService.Load(slotId);
		if (saveGame == null)
		{
			GD.PrintErr($"No save found in slot {slotId}.");
			return;
		}

		try
		{
			InventorySaveCodec.Restore(saveGame.Player.Inventory);
			foreach (var changes in saveGame.World.ItemChanges)
			{
				foreach (var record in changes.Overrides.Concat(changes.AddedItems)) { ItemSaveCodec.Restore(record.Item); }
			}
		}
		catch (Exception error)
		{
			GD.PushError($"Cannot load item state: {error.Message}");
			return;
		}

		ActiveSlotId = slotId;
		ActiveSeed = saveGame.Seed;
		ActiveDungeonDepth = saveGame.DungeonDepth;
		PendingTravelDirection = null;
		_activeSave = saveGame;
		_pendingLoadedSave = saveGame;
		_saveAfterNextSpawn = false;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		_lootRollsConsumed = saveGame.LootRollsConsumed;
		_identification.Initialize(ActiveSeed, saveGame.Identification?.IdentifiedTypeIds, BuildAssignmentMap(saveGame.Identification));
		_identificationReady = true;
		ResolveCharacterClass(saveGame.CharacterClassId);

		GetTree().Paused = false;
		_isSceneTransitioning = true;
		ShowBlackScreen();
		GetTree().ChangeSceneToFile(GameplayScenePath);
	}

	public void ConfigureLevel(Level level)
	{
		ulong levelSeed = GetLevelSeed(ActiveSeed, ActiveDungeonDepth);
		level.ConfigureGeneration(levelSeed, ActiveDungeonDepth);
	}

	public static ulong GetLevelSeed(ulong runSeed, uint depth)
	{
		return runSeed ^ ((ulong)depth * 0x9e3779b97f4a7c15UL);
	}

	/// <summary>
	/// A deterministic RNG for the next loot roll this run. Seeded from the run/depth seed
	/// and a persisted counter so successive drops differ and resume consistently across a
	/// save/load. Rolled results are also persisted on the item, so exact fidelity does not
	/// depend on replaying the same drop order.
	/// </summary>
	public RandomNumberGenerator CreateLootRng()
	{
		var rng = new RandomNumberGenerator
		{
			Seed = GetLevelSeed(ActiveSeed, ActiveDungeonDepth) ^ (_lootRollsConsumed * 0x9e3779b97f4a7c15UL),
		};
		_lootRollsConsumed++;
		return rng;
	}

	public bool CanUseTransition(LevelTravelDirection direction)
	{
		Node scene = GetTree().CurrentScene;
		if (direction == LevelTravelDirection.Down && (scene is Level currentLevel && !currentLevel.CanDescend
			|| scene?.FindChildren("*", "", true, false).OfType<Level>().Any(level => !level.CanDescend) == true))
		{
			return false;
		}
		if (direction == LevelTravelDirection.Up && ActiveDungeonDepth == 1)
		{
			return false;
		}

		double elapsed = (Time.GetTicksMsec() - _stairArrivalTicks) / 1000.0;
		if (elapsed < StairArrivalLockSeconds)
		{
			return false;
		}

		return true;
	}

	public void RegisterStairArrival(Player player)
	{
		_stairArrivalTicks = Time.GetTicksMsec();
		PendingTravelDirection = null;
	}

	public bool ChangeDungeonDepth(LevelTravelDirection direction)
	{
		if (_isSceneTransitioning)
		{
			return false;
		}

		if (!CanUseTransition(direction))
		{
			return false;
		}

		Player player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player == null || player.ActionGate?.IsBusy == true || player.InventoryController.IsCommittingItemOperation || CurrentItemPersistence()?.IsCommitting == true)
		{
			return false;
		}

		EnsureActiveSave();
		try
		{
			StoreLevelItemChanges();
			_activeSave.Player = CapturePlayerForLevelTransition(player);
		}
		catch (Exception error)
		{
			GD.PushError($"Cannot leave floor with invalid item state: {error.Message}");
			return false;
		}
		_pendingLoadedSave = _activeSave;

		if (direction == LevelTravelDirection.Down)
		{
			ActiveDungeonDepth++;
		}
		else if (direction == LevelTravelDirection.Up)
		{
			if (ActiveDungeonDepth > 1)
			{
				ActiveDungeonDepth--;
			}
		}

		PendingTravelDirection = direction;
		_activeSave.DungeonDepth = ActiveDungeonDepth;
		_activeSave.Player.Stats.DungeonDepth = (int)ActiveDungeonDepth;

		GD.Print($"Transitioning {direction} to depth {ActiveDungeonDepth}...");

		_isSceneTransitioning = true;
		FadeOutAndReloadLevel();

		return true;
	}

	public bool IsEntityCleared(string entityId)
	{
		return _activeSave?.World?.ClearedEntityIds?.Contains(entityId) == true;
	}

	public void ClearEntity(string entityId)
	{
		EnsureActiveSave();
		if (!_activeSave.World.ClearedEntityIds.Contains(entityId))
		{
			_activeSave.World.ClearedEntityIds.Add(entityId);
		}
	}

	/// <summary>Records that the player entered (and thus revealed) a room on the current depth.</summary>
	public void MarkRoomRevealed(int roomId)
	{
		RevealedLevelSaveData level = GetOrCreateRevealedLevel();
		if (!level.RevealedRoomIds.Contains(roomId))
		{
			level.RevealedRoomIds.Add(roomId);
		}
	}

	/// <summary>Records that a door at the given connector tile was opened on the current depth.</summary>
	public void MarkDoorOpened(Vector2I connector)
	{
		RevealedLevelSaveData level = GetOrCreateRevealedLevel();
		if (!level.OpenedDoors.Any(door => door.X == connector.X && door.Y == connector.Y))
		{
			level.OpenedDoors.Add(new Vector2ISaveData { X = connector.X, Y = connector.Y });
		}
	}

	public IReadOnlyList<int> GetRevealedRoomIds()
	{
		return FindRevealedLevel()?.RevealedRoomIds ?? (IReadOnlyList<int>)Array.Empty<int>();
	}

	public IReadOnlyList<Vector2I> GetOpenedDoors()
	{
		RevealedLevelSaveData level = FindRevealedLevel();
		return level == null
			? Array.Empty<Vector2I>()
			: level.OpenedDoors.Select(door => new Vector2I(door.X, door.Y)).ToList();
	}

	public void ResetCurrentLevelDebugState() => ResetLevelDebugState(ActiveDungeonDepth);

	/// <summary>Reset one floor's deltas for focused debug playtests without changing run depth.</summary>
	public void ResetLevelDebugState(uint depth)
	{
		if (_activeSave?.World == null)
		{
			return;
		}

		_activeSave.World.ClearedEntityIds.RemoveAll(entityId =>
			entityId.StartsWith($"depth:{depth}:", StringComparison.Ordinal));
		_activeSave.World.RevealedLevels.RemoveAll(level => level.DungeonDepth == depth);
		_activeSave.World.ItemChanges.RemoveAll(level => level.Depth == depth);
		_pendingLoadedSave = null;
	}

	/// <summary>Per-depth differences are applied to freshly regenerated seeded baselines.</summary>
	public LevelItemDeltaSaveData FindLevelItemChanges(uint depth) =>
		_activeSave?.World?.ItemChanges?.FirstOrDefault(change => change.Depth == depth);

	private LevelItemPersistence CurrentItemPersistence() => GetTree().CurrentScene?
		.FindChildren("*", "", true, false).OfType<LevelItemPersistence>().FirstOrDefault();

	private void StoreLevelItemChanges()
	{
		var persistence = CurrentItemPersistence();
		if (persistence == null) { return; }
		var data = persistence.Capture();
		_activeSave.World.ItemChanges.RemoveAll(change => change.Depth == data.Depth);
		_activeSave.World.ItemChanges.Add(data);
	}

	private RevealedLevelSaveData FindRevealedLevel()
	{
		return _activeSave?.World?.RevealedLevels?
			.FirstOrDefault(level => level.DungeonDepth == ActiveDungeonDepth);
	}

	private RevealedLevelSaveData GetOrCreateRevealedLevel()
	{
		EnsureActiveSave();
		RevealedLevelSaveData level = FindRevealedLevel();
		if (level == null)
		{
			level = new RevealedLevelSaveData { DungeonDepth = ActiveDungeonDepth };
			_activeSave.World.RevealedLevels.Add(level);
		}

		return level;
	}

	private void EnsureActiveSave()
	{
		_activeSave ??= new SaveGame
		{
			SlotId = ActiveSlotId,
			RunId = Guid.NewGuid().ToString("N"),
			CreatedAtUtc = DateTime.UtcNow.ToString("O"),
		};
	}

	public bool SaveActiveGame()
	{
		if (!IsGameActive)
		{
			GD.PrintErr("Cannot save because no game slot is active.");
			return false;
		}

		Player player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player == null || player.ActionGate?.IsBusy == true || player.InventoryController.IsCommittingItemOperation || CurrentItemPersistence()?.IsCommitting == true)
		{
			GD.PrintErr("Cannot save while the player is unavailable or an item operation is committing.");
			return false;
		}

		EnsureActiveSave();
		SaveGame saveGame = _activeSave;
		double previousPlayTime = saveGame.PlayTimeSeconds;
		try
		{

			saveGame.Version = SaveGame.CurrentVersion;
			saveGame.SlotId = ActiveSlotId;
			saveGame.CharacterClassId = ActiveCharacterClassId;
			saveGame.SavedAtUtc = DateTime.UtcNow.ToString("O");
			saveGame.Seed = ActiveSeed;
			saveGame.DungeonDepth = ActiveDungeonDepth;
			saveGame.PlayTimeSeconds += GetSessionElapsedSeconds();
			saveGame.LootRollsConsumed = _lootRollsConsumed;
			StoreLevelItemChanges();
			saveGame.Player = CapturePlayer(player);
			saveGame.Identification = CaptureIdentification();

			if (!SaveService.Save(saveGame))
			{
				saveGame.PlayTimeSeconds = previousPlayTime;
				return false;
			}

		}
		catch (Exception error)
		{
			saveGame.PlayTimeSeconds = previousPlayTime;
			GD.PushError($"Cannot capture item state: {error.Message}");
			return false;
		}

		_activeSave = saveGame;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		GD.Print($"Saved game to slot {ActiveSlotId}.");
		return true;
	}

	public void SaveAndReturnToMenu()
	{
		if (SaveActiveGame())
		{
			ReturnToMenu();
		}
	}

	public void ReturnToMenu()
	{
		GetTree().Paused = false;
		SignalBus.EmitGamePaused(false);
		// The menu should not keep a run active just because this autoload survives
		// scene changes. Clearing here releases save data and prevents stale state reuse.
		ClearSession();
		GetTree().ChangeSceneToFile(MenuScenePath);
	}

	/// <summary>
	/// Resolves the run's class from the catalog. Unknown or empty ids use the catalog's default class.
	/// </summary>
	private void ResolveCharacterClass(string characterClassId)
	{
		_characterCatalog ??= CharacterCatalog.LoadDefault();
		ActiveCharacterClass = _characterCatalog?.FindById(characterClassId);
		if (ActiveCharacterClass == null)
		{
			if (!string.IsNullOrEmpty(characterClassId))
			{
				GD.PrintErr($"Unknown character class '{characterClassId}'; falling back to the default class.");
			}

			ActiveCharacterClass = _characterCatalog?.DefaultClass;
		}

		ActiveCharacterClassId = ActiveCharacterClass?.Id ?? SaveGame.DefaultCharacterClassId;
	}

	private void ClearSession()
	{
		ActiveSlotId = 0;
		ActiveSeed = 42;
		ActiveDungeonDepth = 1;
		ActiveCharacterClassId = "";
		ActiveCharacterClass = null;
		PendingTravelDirection = null;
		_activeSave = null;
		_pendingLoadedSave = null;
		_sessionStartedAtMsec = 0;
		_stairArrivalTicks = 0;
		_isSceneTransitioning = false;
		_saveAfterNextSpawn = false;
		_lootRollsConsumed = 0;
		_identificationReady = false;
	}

	private double GetSessionElapsedSeconds()
	{
		return Math.Max(0, (Time.GetTicksMsec() - _sessionStartedAtMsec) / 1000.0);
	}

	private void OnItemConsumed(Player player, ConsumableItem item) => IdentifyItemType(item);

	private void OnItemEquipped(Player player, EquipableItem item) => IdentifyItemType(item);

	/// <summary>
	/// Records discovery of an item's hidden type and notifies the UI. A safe no-op for
	/// items with no hidden identity or a type already discovered. Shared by consuming
	/// a potion/scroll, wearing jewelry, and a future scroll of identify.
	/// </summary>
	public bool IdentifyItemType(Item item)
	{
		if (item is IIdentifiable identifiable && identifiable.HasIdentity && Identification.Identify(identifiable.TypeId))
		{
			GD.Print($"Identified {identifiable.TypeId} as {identifiable.TrueName}.");
			SignalBus.EmitItemIdentified(identifiable.TypeId);
			return true;
		}

		return false;
	}

	private IdentificationSaveData CaptureIdentification()
	{
		var data = new IdentificationSaveData
		{
			IdentifiedTypeIds = Identification.GetIdentifiedTypeIds().ToList(),
		};

		foreach (KeyValuePair<string, string> assignment in Identification.GetAssignmentDescriptors())
		{
			data.Assignments.Add(new AppearanceAssignmentSaveData
			{
				TypeId = assignment.Key,
				AppearanceDescriptor = assignment.Value,
			});
		}

		return data;
	}

	private static Dictionary<string, string> BuildAssignmentMap(IdentificationSaveData data)
	{
		var map = new Dictionary<string, string>();
		if (data?.Assignments != null)
		{
			foreach (AppearanceAssignmentSaveData assignment in data.Assignments)
			{
				if (!string.IsNullOrEmpty(assignment.TypeId))
				{
					map[assignment.TypeId] = assignment.AppearanceDescriptor;
				}
			}
		}

		return map;
	}

	private void OnPlayerSpawned(Player player)
	{
		if (_pendingLoadedSave?.Player != null)
		{
			// Loading still instantiates through the authored spawn point; the saved
			// transform is applied immediately before the level is revealed.
			ApplyPlayerSave(player, _pendingLoadedSave.Player);
			_pendingLoadedSave = null;
		}
		else if (_saveAfterNextSpawn)
		{
			_saveAfterNextSpawn = false;
			SaveActiveGame();
		}

		CallDeferred(MethodName.FadeInAfterLevelSettled);
	}

	private async void FadeOutAndReloadLevel()
	{
		GetTree().Paused = false;
		await FadeToBlack();

		// This can start from the transition trigger's physics callback (BodyEntered), so
		// defer the reload to idle — reloading now would free the current scene's
		// collision bodies (including the player) mid-physics-step.
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	private async void FadeInAfterLevelSettled()
	{
		// Player spawning can happen while Main is still wiring the camera. Let the
		// scene finish _Ready and process a frame before revealing the level.
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await FadeFromBlack();
		_isSceneTransitioning = false;
	}

	private async Task FadeToBlack()
	{
		EnsureLevelFadeOverlay();
		if (!_levelFadeLayer.Visible)
		{
			_levelFadeRect.Color = TransparentBlack;
		}

		_levelFadeLayer.Visible = true;
		await TweenFadeAlpha(1f, LevelFadeOutSeconds);
	}

	private void ShowBlackScreen()
	{
		EnsureLevelFadeOverlay();
		_levelFadeTween?.Kill();
		_levelFadeRect.Color = Colors.Black;
		_levelFadeLayer.Visible = true;
	}

	private async Task FadeFromBlack()
	{
		EnsureLevelFadeOverlay();
		if (!_levelFadeLayer.Visible && _levelFadeRect.Color.A <= 0f)
		{
			return;
		}

		_levelFadeLayer.Visible = true;
		await TweenFadeAlpha(0f, LevelFadeInSeconds);
		_levelFadeLayer.Visible = false;
	}

	private async Task TweenFadeAlpha(float targetAlpha, double seconds)
	{
		_levelFadeTween?.Kill();
		_levelFadeTween = CreateTween();
		_levelFadeTween.TweenProperty(_levelFadeRect, "color:a", targetAlpha, seconds)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.InOut);
		await ToSignal(_levelFadeTween, Tween.SignalName.Finished);
	}

	private void EnsureLevelFadeOverlay()
	{
		if (GodotObject.IsInstanceValid(_levelFadeLayer) && GodotObject.IsInstanceValid(_levelFadeRect))
		{
			return;
		}

		_levelFadeLayer = new CanvasLayer
		{
			Name = "LevelTransitionFade",
			Layer = LevelFadeLayer,
			ProcessMode = ProcessModeEnum.Always,
			Visible = false,
		};

		_levelFadeRect = new ColorRect
		{
			Name = "FadeRect",
			Color = TransparentBlack,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_levelFadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		AddChild(_levelFadeLayer);
		_levelFadeLayer.AddChild(_levelFadeRect);
	}

	private static PlayerSaveData CapturePlayer(Player player)
	{
		return new PlayerSaveData
		{
			Position = CaptureVector3(player.GlobalPosition),
			Rotation = CaptureVector3(player.Rotation),
			Stats = CaptureStats(player.Stats),
			Inventory = CaptureInventory(player.Inventory),
			Actions = CaptureActions(player),
		};
	}

	private static PlayerSaveData CapturePlayerForLevelTransition(Player player)
	{
		return new PlayerSaveData
		{
			ApplyTransform = false,
			Stats = CaptureStats(player.Stats),
			Inventory = CaptureInventory(player.Inventory),
			Actions = CaptureActions(player),
		};
	}

	private static PlayerStatsSaveData CaptureStats(PlayerStats stats)
	{
		return new PlayerStatsSaveData
		{
			Health = stats.Health,
			Xp = stats.Xp,
			XpLevel = stats.XpLevel,
			Gold = stats.Gold,
			DungeonDepth = stats.DungeonDepth,
			BaseStrength = stats.BaseStrength,
			BaseDexterity = stats.BaseDexterity,
			BaseVitality = stats.BaseVitality,
			BaseIntelligence = stats.BaseIntelligence,
			BaseSpeed = stats.BaseSpeed,
			BaseMaxHealth = stats.BaseMaxHealth,
			BaseMinDamage = stats.BaseMinDamage,
			BaseMaxDamage = stats.BaseMaxDamage,
			BaseCritChance = stats.BaseCritChance,
			BaseArmor = stats.BaseArmor,
			XpModifier = stats.XpModifier,
			GoldModifier = stats.GoldModifier,
		};
	}

	private static InventorySaveData CaptureInventory(Inventory inventory) => InventorySaveCodec.Capture(inventory);

	private static void ApplyPlayerSave(Player player, PlayerSaveData saveData)
	{
		player.BuffController.ClearBuffs();
		ApplyInventory(player, saveData.Inventory);
		ApplyActions(player, saveData.Actions);
		ApplyStats(player.Stats, saveData.Stats);
		player.StatsController.SyncStats();
		if (saveData.ApplyTransform)
		{
			player.GlobalPosition = ToVector3(saveData.Position);
			player.Rotation = ToVector3(saveData.Rotation);
		}
	}

	private static PlayerActionSaveData CaptureActions(Player player)
	{
		var data = new PlayerActionSaveData();
		if (player.QuickItemController != null)
		{
			for (int i = 0; i < 4; i++)
			{
				var binding = player.QuickItemController.GetBinding(i);
				data.QuickSlots.Add(new QuickItemBindingSaveData
				{
					SlotIndex = i,
					Kind = binding.Kind.ToString(),
					DefinitionPath = binding.DefinitionPath,
					InstanceId = binding.InstanceId
				});
			}

			foreach (var kvp in player.QuickItemController.GetVarietyCooldowns())
			{
				data.VarietyCooldowns[kvp.Key] = kvp.Value;
			}
		}

		data.DodgeCooldownRemaining = player.DodgeComponent?.CooldownRemaining ?? 0f;
		data.ParryRearmRemaining = player.GuardComponent?.ParryRearmRemaining ?? 0f;
		data.RecoveryLockoutRemaining = Mathf.Max(player.ActionGate?.StaggerRemaining ?? 0,
			Mathf.Max(player.AttackController?.RecoveryRemaining ?? 0,
			Mathf.Max(player.DodgeComponent?.RecoveryRemaining ?? 0, player.GuardComponent?.RecoveryRemaining ?? 0)));

		return data;
	}

	private static void ApplyActions(Player player, PlayerActionSaveData data)
	{
		if (player.QuickItemController == null || data == null) return;
		data.ValidateCombatTimers();
		player.QuickItemController.IsRestoringSave = true;
		try
		{
			if (data.QuickSlots != null)
			{
				foreach (var slotData in data.QuickSlots)
				{
					if (slotData.SlotIndex < 0 || slotData.SlotIndex >= 4) continue;
					if (System.Enum.TryParse<QuickItemBindingKind>(slotData.Kind, out var kind))
					{
						switch (kind)
						{
							case QuickItemBindingKind.Variety:
								player.QuickItemController.RestoreSlot(slotData.SlotIndex, QuickItemBinding.ForVariety(slotData.DefinitionPath));
								break;
							case QuickItemBindingKind.Instance:
								player.QuickItemController.RestoreSlot(slotData.SlotIndex, QuickItemBinding.ForInstance(slotData.InstanceId, slotData.DefinitionPath));
								break;
							default:
								player.QuickItemController.RestoreSlot(slotData.SlotIndex, QuickItemBinding.Unassigned());
								break;
						}
					}
				}
			}

			if (data.VarietyCooldowns != null)
			{
				player.QuickItemController.RestoreVarietyCooldowns(data.VarietyCooldowns);
			}

			player.QuickItemController.RefreshCounts();
		}
		finally
		{
			player.QuickItemController.IsRestoringSave = false;
		}

		player.ActionGate?.CancelCurrent(ActionInterruptReason.Invalidated);
		player.AttackController?.CancelAttack();
		player.MovementComponent?.ResetTransientMotion();
		player.DodgeComponent?.RestoreCooldown(data.DodgeCooldownRemaining);
		player.GuardComponent?.RestoreCooldown(data.ParryRearmRemaining);
		player.InputController?.RequireFreshDefenseInput();
		if (data.RecoveryLockoutRemaining > 0) player.ActionGate?.TryStagger(data.RecoveryLockoutRemaining);
	}

	private static void ApplyInventory(Player player, InventorySaveData saveData)
	{
		Inventory restored = InventorySaveCodec.Restore(saveData);
		Inventory inventory = player.Inventory;
		player.ActionManager.ClearActions();
		foreach (EquipmentSlot slot in inventory.EquippedItems.Keys.ToArray()) { inventory.Unequip(slot); }
		inventory.Items.Clear();
		inventory.Capacity = restored.Capacity;
		foreach (InventoryItemSlot entry in restored.Items) { inventory.Items.Add(entry); }
		foreach (EquippedItemSaveData equipment in saveData.EquippedItems)
		{
			var entry = inventory.Items.First(i => i.Item.InstanceId == equipment.ItemInstanceId);
			inventory.Equip(entry, Enum.Parse<EquipmentSlot>(equipment.Slot));
		}
		inventory.EmitChanged();
	}

	private static void ApplyStats(PlayerStats stats, PlayerStatsSaveData saveData)
	{
		stats.Health = saveData.Health;
		stats.Xp = saveData.Xp;
		stats.XpLevel = saveData.XpLevel;
		stats.Gold = saveData.Gold;
		stats.DungeonDepth = saveData.DungeonDepth;
		stats.BaseStrength = saveData.BaseStrength;
		stats.BaseDexterity = saveData.BaseDexterity;
		stats.BaseVitality = saveData.BaseVitality;
		stats.BaseIntelligence = saveData.BaseIntelligence;
		stats.BaseSpeed = saveData.BaseSpeed;
		stats.BaseMaxHealth = saveData.BaseMaxHealth;
		stats.BaseMinDamage = saveData.BaseMinDamage;
		stats.BaseMaxDamage = saveData.BaseMaxDamage;
		stats.BaseCritChance = saveData.BaseCritChance;
		stats.BaseArmor = saveData.BaseArmor;
		stats.XpModifier = saveData.XpModifier;
		stats.GoldModifier = saveData.GoldModifier;
	}

	private static Vector3SaveData CaptureVector3(Vector3 vector)
	{
		return new Vector3SaveData
		{
			X = vector.X,
			Y = vector.Y,
			Z = vector.Z,
		};
	}

	private static Vector3 ToVector3(Vector3SaveData vector)
	{
		return new Vector3(vector.X, vector.Y, vector.Z);
	}
}