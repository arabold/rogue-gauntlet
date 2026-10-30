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
	public uint ActiveDungeonDepth { get; private set; } = 1;
	public bool IsGameActive => ActiveSlotId > 0;

	private RunState _run;

	/// <summary>
	/// The active run, handed to each level. Starting or loading a game creates it; anything that plays without one
	/// (a scene run directly, or back at the menu) gets the default run on first use. UI-wide item display reads
	/// identification from here; gameplay reads it from its level.
	/// </summary>
	public RunState Run => _run ??= new RunState(RunState.DefaultSeed);

	/// <summary>Debug harnesses: replaces the active run with an unsaved one, so gameplay and UI share its state.</summary>
	public void StartUnsavedRun(ulong runSeed)
	{
		if (IsGameActive) throw new InvalidOperationException("An unsaved run cannot replace an active game.");
		_run = new RunState(runSeed);
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
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		base._ExitTree();
	}

	/// <summary>Starts a run in <paramref name="slotId"/>; <paramref name="seed"/> replays a known run instead of a fresh one.</summary>
	public void StartNewGame(int slotId, string characterClassId = null, ulong? seed = null)
	{
		if (_isSceneTransitioning)
		{
			return;
		}

		SaveService.GetSlotPath(slotId);
		ActiveSlotId = slotId;
		_run = new RunState(seed ?? RunState.CreateSeed());
		GD.Print($"Starting run with seed {Run.RunSeed}.");
		ActiveDungeonDepth = 1;
		PendingTravelDirection = LevelTravelDirection.Down;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		_pendingLoadedSave = null;
		_saveAfterNextSpawn = true;
		ResolveCharacterClass(characterClassId);

		// The hero knows their own supplies: class starting consumables are never disguised.
		foreach (string typeId in ActiveCharacterClass?.PreIdentifiedTypeIds ?? Array.Empty<string>())
		{
			Run.Identification.Identify(typeId);
		}

		string now = DateTime.UtcNow.ToString("O");
		_activeSave = new SaveGame
		{
			SlotId = slotId,
			RunId = Guid.NewGuid().ToString("N"),
			CreatedAtUtc = now,
			SavedAtUtc = now,
			CharacterClassId = ActiveCharacterClassId,
			Seed = Run.RunSeed,
			DungeonDepth = ActiveDungeonDepth,
		};

		GetTree().Paused = false;
		_isSceneTransitioning = true;
		ShowBlackScreen();
		GetTree().ChangeSceneToFile(GameplayScenePath);
	}

	/// <summary>
	/// Debug: starts over from depth 1 with <paramref name="seed"/>, reproducing its floors, encounters, source rewards and
	/// disguises. An active game restarts in its slot and class, replacing that save; otherwise the scene reloads an
	/// unsaved run.
	/// </summary>
	public void ReplayRun(ulong seed)
	{
		if (IsGameActive)
		{
			StartNewGame(ActiveSlotId, ActiveCharacterClassId, seed);
			return;
		}
		if (_isSceneTransitioning)
		{
			return;
		}

		ClearSession();
		_run = new RunState(seed);
		GD.Print($"Replaying unsaved run with seed {seed}.");
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
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

		var identification = new IdentificationService();
		identification.Initialize(saveGame.Seed, saveGame.Identification?.IdentifiedTypeIds, BuildAssignmentMap(saveGame.Identification));
		ActiveSlotId = slotId;
		_run = new RunState(saveGame.Seed, saveGame.LootRollsConsumed, saveGame.World.ClearedEntityIds, identification);
		ActiveDungeonDepth = saveGame.DungeonDepth;
		PendingTravelDirection = null;
		_activeSave = saveGame;
		_pendingLoadedSave = saveGame;
		_saveAfterNextSpawn = false;
		_sessionStartedAtMsec = Time.GetTicksMsec();
		ResolveCharacterClass(saveGame.CharacterClassId);

		GetTree().Paused = false;
		_isSceneTransitioning = true;
		ShowBlackScreen();
		GetTree().ChangeSceneToFile(GameplayScenePath);
	}

	/// <summary>Hands the level this run and the floor's saved deltas; a generation failure returns to the menu.</summary>
	public void ConfigureLevel(Level level)
	{
		level.ConfigureGeneration(Run, ActiveDungeonDepth, FindLevelItemChanges(ActiveDungeonDepth), GetLevelExploration(ActiveDungeonDepth));
		level.SubscribeUntilExit(level, l => l.GenerationFailed += OnGenerationFailed, l => l.GenerationFailed -= OnGenerationFailed);
	}

	private void OnGenerationFailed(string reason) => CallDeferred(MethodName.ReturnToMenu);

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
		if (player == null || player.ActionGate?.IsBusy == true || player.InventoryController.IsCommittingItemOperation || CurrentLevel()?.IsCommittingFloorChanges == true)
		{
			return false;
		}

		EnsureActiveSave();
		try
		{
			StoreFloorDeltas();
			_activeSave.Player = PlayerSaveCodec.Capture(player, includeTransform: false);
		}
		catch (Exception error)
		{
			GD.PushError($"Cannot leave floor with invalid player or floor state: {error.Message}");
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

		GD.Print($"Transitioning {direction} to depth {ActiveDungeonDepth}...");

		_isSceneTransitioning = true;
		FadeOutAndReloadLevel();

		return true;
	}

	private RevealedLevelSaveData GetLevelExploration(uint dungeonDepth) => _activeSave?.World?.RevealedLevels?
		.FirstOrDefault(level => level.DungeonDepth == dungeonDepth);

	public void ResetCurrentLevelDebugState() => ResetLevelDebugState(ActiveDungeonDepth);

	/// <summary>Reset one floor's deltas for focused debug playtests without changing run depth.</summary>
	public void ResetLevelDebugState(uint depth)
	{
		Run.ForgetCleared(depth);
		if (_activeSave?.World == null)
		{
			return;
		}

		_activeSave.World.RevealedLevels.RemoveAll(level => level.DungeonDepth == depth);
		_activeSave.World.ItemChanges.RemoveAll(level => level.Depth == depth);
		_pendingLoadedSave = null;
	}

	/// <summary>Per-depth differences are applied to freshly regenerated seeded baselines.</summary>
	private LevelItemDeltaSaveData FindLevelItemChanges(uint depth) =>
		_activeSave?.World?.ItemChanges?.FirstOrDefault(change => change.Depth == depth);

	private Level CurrentLevel() => GetTree().CurrentScene?
		.FindChildren("*", "", true, false).OfType<Level>().FirstOrDefault();

	/// <summary>Store the current floor's item and exploration deltas before travel or disk capture.</summary>
	private void StoreFloorDeltas()
	{
		var level = CurrentLevel();
		if (level == null) { return; }
		var (items, exploration) = level.CaptureFloorDeltas();
		_activeSave.World.ItemChanges.RemoveAll(change => change.Depth == items.Depth);
		_activeSave.World.ItemChanges.Add(items);
		if (exploration.DungeonDepth != ActiveDungeonDepth) { return; }
		_activeSave.World.RevealedLevels.RemoveAll(revealed => revealed.DungeonDepth == exploration.DungeonDepth);
		_activeSave.World.RevealedLevels.Add(exploration);
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
		if (player == null || player.ActionGate?.IsBusy == true || player.InventoryController.IsCommittingItemOperation || CurrentLevel()?.IsCommittingFloorChanges == true)
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
			saveGame.Seed = Run.RunSeed;
			saveGame.DungeonDepth = ActiveDungeonDepth;
			saveGame.PlayTimeSeconds += GetSessionElapsedSeconds();
			saveGame.LootRollsConsumed = Run.LootRollsConsumed;
			saveGame.World.ClearedEntityIds = Run.CaptureClearedIds();
			StoreFloorDeltas();
			saveGame.Player = PlayerSaveCodec.Capture(player, includeTransform: true);
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
			GD.PushError($"Cannot capture player or floor state: {error.Message}");
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
		_run = null;
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
	}

	private double GetSessionElapsedSeconds()
	{
		return Math.Max(0, (Time.GetTicksMsec() - _sessionStartedAtMsec) / 1000.0);
	}

	private IdentificationSaveData CaptureIdentification()
	{
		var data = new IdentificationSaveData
		{
			IdentifiedTypeIds = Run.Identification.GetIdentifiedTypeIds().ToList(),
		};

		foreach (KeyValuePair<string, string> assignment in Run.Identification.GetAssignmentDescriptors())
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
			PlayerSaveCodec.Apply(player, _pendingLoadedSave.Player);
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
}
