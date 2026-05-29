using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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

	public enum LevelTravelDirection
	{
		None,
		Up,
		Down
	}

	public LevelTravelDirection PendingTravelDirection { get; private set; } = LevelTravelDirection.None;
	private double _stairArrivalTicks;
	private const double StairArrivalLockSeconds = 1.0;

	private SaveGame _activeSave;
	private SaveGame _pendingLoadedSave;
	private double _sessionStartedAtMsec;

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
		SaveService.GetSlotPath(slotId);
		ActiveSlotId = slotId;
		ActiveSeed = (ulong)DateTime.UtcNow.Ticks;
		ActiveDungeonDepth = 1;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		_pendingLoadedSave = null;

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
		GetTree().ChangeSceneToFile(GameplayScenePath);
	}

	public void LoadGame(int slotId)
	{
		SaveGame saveGame = SaveService.Load(slotId);
		if (saveGame == null)
		{
			GD.PrintErr($"No save found in slot {slotId}.");
			return;
		}

		ActiveSlotId = slotId;
		ActiveSeed = saveGame.Seed;
		ActiveDungeonDepth = saveGame.DungeonDepth;
		_activeSave = saveGame;
		_pendingLoadedSave = saveGame;
		_sessionStartedAtMsec = Time.GetTicksMsec();

		GetTree().Paused = false;
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
		PendingTravelDirection = LevelTravelDirection.None;
	}

	public bool ChangeDungeonDepth(LevelTravelDirection direction)
	{
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

		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();

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

	public void SaveActiveGame()
	{
		if (!IsGameActive)
		{
			GD.PrintErr("Cannot save because no game slot is active.");
			return;
		}

		Player player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player == null)
		{
			GD.PrintErr("Cannot save because no player is active.");
			return;
		}

		EnsureActiveSave();
		SaveGame saveGame = _activeSave;

		saveGame.Version = SaveGame.CurrentVersion;
		saveGame.SlotId = ActiveSlotId;
		saveGame.SavedAtUtc = DateTime.UtcNow.ToString("O");
		saveGame.Seed = ActiveSeed;
		saveGame.DungeonDepth = ActiveDungeonDepth;
		saveGame.PlayTimeSeconds += GetSessionElapsedSeconds();
		saveGame.Player = CapturePlayer(player);

		SaveService.Save(saveGame);
		_activeSave = saveGame;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		GD.Print($"Saved game to slot {ActiveSlotId}.");
	}

	public void SaveAndReturnToMenu()
	{
		SaveActiveGame();
		ReturnToMenu();
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
		PendingTravelDirection = LevelTravelDirection.None;
		_activeSave = null;
		_pendingLoadedSave = null;
		_sessionStartedAtMsec = 0;
		_stairArrivalTicks = 0;
	}

	private double GetSessionElapsedSeconds()
	{
		return Math.Max(0, (Time.GetTicksMsec() - _sessionStartedAtMsec) / 1000.0);
	}

	private void OnPlayerSpawned(Player player)
	{
		if (_pendingLoadedSave?.Player == null)
		{
			return;
		}

		ApplyPlayerSave(player, _pendingLoadedSave.Player);
		_pendingLoadedSave = null;
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
			BaseSpeed = stats.BaseSpeed,
			BaseMaxHealth = stats.BaseMaxHealth,
			BaseAccuracy = stats.BaseAccuracy,
			BaseMinDamage = stats.BaseMinDamage,
			BaseMaxDamage = stats.BaseMaxDamage,
			BaseCritChance = stats.BaseCritChance,
			BaseArmor = stats.BaseArmor,
			BaseEvasion = stats.BaseEvasion,
			SpeedModifier = stats.SpeedModifier,
			HealthModifier = stats.HealthModifier,
			XpModifier = stats.XpModifier,
			GoldModifier = stats.GoldModifier,
			DamageModifier = stats.DamageModifier,
			CritModifier = stats.CritModifier,
			ArmorModifier = stats.ArmorModifier,
			AccuracyModifier = stats.AccuracyModifier,
		};
	}

	private static InventorySaveData CaptureInventory(Inventory inventory)
	{
		var saveData = new InventorySaveData();
		var savedItemIndexes = new Dictionary<InventoryItemSlot, int>();
		for (int i = 0; i < inventory.Items.Count; i++)
		{
			InventoryItemSlot itemSlot = inventory.Items[i];
			if (itemSlot?.Item == null || string.IsNullOrEmpty(itemSlot.Item.ResourcePath))
			{
				continue;
			}

			savedItemIndexes[itemSlot] = saveData.Items.Count;
			saveData.Items.Add(new InventoryItemSaveData
			{
				ItemPath = itemSlot.Item.ResourcePath,
				Quantity = itemSlot.Quantity,
			});
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
		stats.BaseSpeed = saveData.BaseSpeed;
		stats.BaseMaxHealth = saveData.BaseMaxHealth;
		stats.BaseAccuracy = saveData.BaseAccuracy;
		stats.BaseMinDamage = saveData.BaseMinDamage;
		stats.BaseMaxDamage = saveData.BaseMaxDamage;
		stats.BaseCritChance = saveData.BaseCritChance;
		stats.BaseArmor = saveData.BaseArmor;
		stats.BaseEvasion = saveData.BaseEvasion;
		stats.SpeedModifier = saveData.SpeedModifier;
		stats.HealthModifier = saveData.HealthModifier;
		stats.XpModifier = saveData.XpModifier;
		stats.GoldModifier = saveData.GoldModifier;
		stats.DamageModifier = saveData.DamageModifier;
		stats.CritModifier = saveData.CritModifier;
		stats.ArmorModifier = saveData.ArmorModifier;
		stats.AccuracyModifier = saveData.AccuracyModifier;
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
