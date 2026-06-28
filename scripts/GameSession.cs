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

	/// <summary>Per-run hidden-identity state for potions, scrolls, and similar items.</summary>
	public IdentificationService Identification { get; } = new();

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
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		base._ExitTree();
	}

	public void StartNewGame(int slotId)
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
		Identification.Initialize(ActiveSeed);

		string now = DateTime.UtcNow.ToString("O");
		_activeSave = new SaveGame
		{
			SlotId = slotId,
			RunId = Guid.NewGuid().ToString("N"),
			CreatedAtUtc = now,
			SavedAtUtc = now,
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

		ActiveSlotId = slotId;
		ActiveSeed = saveGame.Seed;
		ActiveDungeonDepth = saveGame.DungeonDepth;
		PendingTravelDirection = null;
		_activeSave = saveGame;
		_pendingLoadedSave = saveGame;
		_saveAfterNextSpawn = false;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		_lootRollsConsumed = saveGame.LootRollsConsumed;
		Identification.Initialize(ActiveSeed, saveGame.Identification?.IdentifiedTypeIds, BuildAssignmentMap(saveGame.Identification));

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
		if (player == null)
		{
			return false;
		}

		EnsureActiveSave();
		_activeSave.Player = CapturePlayerForLevelTransition(player);
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

	public void ResetCurrentLevelDebugState()
	{
		if (_activeSave?.World == null)
		{
			return;
		}

		_activeSave.World.ClearedEntityIds.RemoveAll(entityId =>
			entityId.StartsWith($"depth:{ActiveDungeonDepth}:", StringComparison.Ordinal));
		_activeSave.World.RevealedLevels.RemoveAll(level => level.DungeonDepth == ActiveDungeonDepth);
		_pendingLoadedSave = null;
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
		if (player == null)
		{
			GD.PrintErr("Cannot save because no player is active.");
			return false;
		}

		EnsureActiveSave();
		SaveGame saveGame = _activeSave;

		saveGame.Version = SaveGame.CurrentVersion;
		saveGame.SlotId = ActiveSlotId;
		saveGame.SavedAtUtc = DateTime.UtcNow.ToString("O");
		saveGame.Seed = ActiveSeed;
		saveGame.DungeonDepth = ActiveDungeonDepth;
		saveGame.PlayTimeSeconds += GetSessionElapsedSeconds();
		saveGame.LootRollsConsumed = _lootRollsConsumed;
		saveGame.Player = CapturePlayer(player);
		saveGame.Identification = CaptureIdentification();

		if (!SaveService.Save(saveGame))
		{
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

	private void ClearSession()
	{
		ActiveSlotId = 0;
		ActiveSeed = 42;
		ActiveDungeonDepth = 1;
		PendingTravelDirection = null;
		_activeSave = null;
		_pendingLoadedSave = null;
		_sessionStartedAtMsec = 0;
		_stairArrivalTicks = 0;
		_isSceneTransitioning = false;
		_saveAfterNextSpawn = false;
		_lootRollsConsumed = 0;
	}

	private double GetSessionElapsedSeconds()
	{
		return Math.Max(0, (Time.GetTicksMsec() - _sessionStartedAtMsec) / 1000.0);
	}

	private void OnItemConsumed(Player player, ConsumableItem item)
	{
		if (item is IdentifiableItem identifiable
			&& identifiable.HasIdentity
			&& Identification.Identify(identifiable.TypeId))
		{
			GD.Print($"Identified {identifiable.TypeId} as {identifiable.TrueName}.");
			SignalBus.EmitItemIdentified(identifiable.TypeId);
		}
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
		};
	}

	private static PlayerSaveData CapturePlayerForLevelTransition(Player player)
	{
		return new PlayerSaveData
		{
			ApplyTransform = false,
			Stats = CaptureStats(player.Stats),
			Inventory = CaptureInventory(player.Inventory),
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
			BaseAccuracy = stats.BaseAccuracy,
			BaseMinDamage = stats.BaseMinDamage,
			BaseMaxDamage = stats.BaseMaxDamage,
			BaseCritChance = stats.BaseCritChance,
			BaseArmor = stats.BaseArmor,
			BaseEvasion = stats.BaseEvasion,
			XpModifier = stats.XpModifier,
			GoldModifier = stats.GoldModifier,
		};
	}

	private static InventorySaveData CaptureInventory(Inventory inventory)
	{
		var saveData = new InventorySaveData();
		var savedItemIndexes = new Dictionary<InventoryItemSlot, int>();
		for (int i = 0; i < inventory.Items.Count; i++)
		{
			InventoryItemSlot itemSlot = inventory.Items[i];
			if (itemSlot?.Item == null)
			{
				continue;
			}

			// A rolled instance has an empty ResourcePath but knows its source definition;
			// fall back to that so the base item can be reloaded and re-stamped on load.
			var rolled = itemSlot.Item as EquipableItem;
			string path = !string.IsNullOrEmpty(itemSlot.Item.ResourcePath)
				? itemSlot.Item.ResourcePath
				: rolled?.SourceDefinitionPath;
			if (string.IsNullOrEmpty(path))
			{
				continue;
			}

			var itemData = new InventoryItemSaveData
			{
				ItemPath = path,
				Quantity = itemSlot.Quantity,
			};

			if (rolled != null && !string.IsNullOrEmpty(rolled.SourceDefinitionPath))
			{
				itemData.Rarity = (int)rolled.Rarity;
				itemData.Affixes = CaptureAffixes(rolled.Affixes);
			}

			savedItemIndexes[itemSlot] = saveData.Items.Count;
			saveData.Items.Add(itemData);
		}

		for (int i = 0; i < inventory.Items.Count; i++)
		{
			InventoryItemSlot itemSlot = inventory.Items[i];
			if (!savedItemIndexes.TryGetValue(itemSlot, out int savedItemIndex))
			{
				continue;
			}

			foreach ((EquipmentSlot slot, InventoryItemSlot equippedItemSlot) in inventory.EquippedItems)
			{
				if (equippedItemSlot == itemSlot)
				{
					saveData.EquippedItems.Add(new EquippedItemSaveData
					{
						ItemIndex = savedItemIndex,
						Slot = slot.ToString(),
					});
				}
			}
		}

		return saveData;
	}

	private static List<RolledAffixSaveData> CaptureAffixes(Godot.Collections.Array<RolledAffix> affixes)
	{
		var list = new List<RolledAffixSaveData>();
		if (affixes == null)
		{
			return list;
		}

		foreach (RolledAffix affix in affixes)
		{
			if (affix == null)
			{
				continue;
			}

			var data = new RolledAffixSaveData { NameFragment = affix.NameFragment, Kind = (int)affix.Kind };
			if (affix.Modifiers != null)
			{
				foreach (StatModifier modifier in affix.Modifiers)
				{
					if (modifier != null)
					{
						data.Modifiers.Add(new StatModifierSaveData
						{
							Stat = (int)modifier.Stat,
							Op = (int)modifier.Op,
							Value = modifier.Value,
						});
					}
				}
			}

			list.Add(data);
		}

		return list;
	}

	private static Godot.Collections.Array<RolledAffix> BuildAffixes(List<RolledAffixSaveData> saved)
	{
		var affixes = new Godot.Collections.Array<RolledAffix>();
		if (saved == null)
		{
			return affixes;
		}

		foreach (RolledAffixSaveData data in saved)
		{
			var modifiers = new List<StatModifier>();
			foreach (StatModifierSaveData modifier in data.Modifiers)
			{
				modifiers.Add(new StatModifier
				{
					Stat = (StatType)modifier.Stat,
					Op = (ModifierOp)modifier.Op,
					Value = modifier.Value,
				});
			}

			affixes.Add(new RolledAffix
			{
				NameFragment = data.NameFragment,
				Kind = (AffixKind)data.Kind,
				Modifiers = modifiers.ToArray(),
			});
		}

		return affixes;
	}

	private static void ApplyPlayerSave(Player player, PlayerSaveData saveData)
	{
		ClearBuffs(player);
		ApplyInventory(player, saveData.Inventory);
		ApplyStats(player.Stats, saveData.Stats);
		player.StatsController.SyncStats();
		if (saveData.ApplyTransform)
		{
			player.GlobalPosition = ToVector3(saveData.Position);
			player.Rotation = ToVector3(saveData.Rotation);
		}
	}

	private static void ApplyInventory(Player player, InventorySaveData saveData)
	{
		Inventory inventory = player.Inventory;
		player.ActionManager.ClearActions();
		foreach (EquipmentSlot slot in inventory.EquippedItems.Keys.ToArray())
		{
			inventory.EquippedItems[slot] = null;
		}

		inventory.Items.Clear();
		foreach (InventoryItemSaveData itemSaveData in saveData.Items)
		{
			Item item = ResourceLoader.Load<Item>(itemSaveData.ItemPath);
			if (item == null)
			{
				GD.PrintErr($"Could not load saved item: {itemSaveData.ItemPath}");
				continue;
			}

			// Reconstruct a rolled instance: duplicate the base definition and re-stamp the
			// saved rarity and affixes. Plain items (Rarity -1, no affixes) load as-is.
			if (item is EquipableItem definition
				&& (itemSaveData.Rarity >= 0 || itemSaveData.Affixes.Count > 0))
			{
				var instance = (EquipableItem)definition.Duplicate(true);
				instance.SourceDefinitionPath = itemSaveData.ItemPath;
				if (itemSaveData.Rarity >= 0)
				{
					instance.Rarity = (EquipableItemRarity)itemSaveData.Rarity;
				}

				instance.Affixes = BuildAffixes(itemSaveData.Affixes);
				item = instance;
			}

			inventory.Items.Add(new InventoryItemSlot
			{
				Item = item,
				Quantity = itemSaveData.Quantity,
			});
		}

		foreach (EquippedItemSaveData equippedItemSaveData in saveData.EquippedItems)
		{
			if (equippedItemSaveData.ItemIndex < 0 || equippedItemSaveData.ItemIndex >= inventory.Items.Count)
			{
				continue;
			}

			if (Enum.TryParse(equippedItemSaveData.Slot, out EquipmentSlot slot))
			{
				inventory.Equip(inventory.Items[equippedItemSaveData.ItemIndex], slot);
			}
			else
			{
				inventory.Equip(inventory.Items[equippedItemSaveData.ItemIndex]);
			}
		}
	}

	private static void ClearBuffs(Player player)
	{
		foreach (ActiveBuff activeBuff in player.BuffController.ActiveBuffs.ToArray())
		{
			activeBuff.Deactivate();
			player.BuffController.ActiveBuffs.Remove(activeBuff);
			player.BuffController.RemoveChild(activeBuff);
			activeBuff.QueueFree();
		}
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
		stats.BaseAccuracy = saveData.BaseAccuracy;
		stats.BaseMinDamage = saveData.BaseMinDamage;
		stats.BaseMaxDamage = saveData.BaseMaxDamage;
		stats.BaseCritChance = saveData.BaseCritChance;
		stats.BaseArmor = saveData.BaseArmor;
		stats.BaseEvasion = saveData.BaseEvasion;
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
