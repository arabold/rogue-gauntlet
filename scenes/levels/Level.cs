using System.Collections.Generic;
using System.Linq;
using Godot;

[Tool]
public partial class Level : Node
{
	[Export] public bool AutoGenerateOnReady { get; set; } = true;

	[Export] public MapGenerator MapGenerator { get; set; }
	/// <summary>Optional scene environment whose ambient illumination follows this floor's darkness.</summary>
	[Export] public WorldEnvironment LightingEnvironment { get; set; }
	[Signal] public delegate void DarknessChangedEventHandler(bool isDark);
	/// <summary>The floor could not be generated or its saved deltas could not be restored; the session decides what follows.</summary>
	[Signal] public delegate void GenerationFailedEventHandler(string reason);
	/// <summary>Current floor illumination override; debug changes last for this level instance only.</summary>
	public bool IsDark { get; private set; }
	private float _normalAmbientEnergy;
	private Environment _lightingEnvironment;

	/// <summary>Changes floor illumination and notifies local player/fixture lighting without changing exploration.</summary>
	public void SetDark(bool isDark)
	{
		if (IsDark == isDark) { return; }
		IsDark = isDark;
		if (_lightingEnvironment != null)
		{
			_lightingEnvironment.AmbientLightEnergy = isDark ? 0f : _normalAmbientEnergy;
		}
		EmitSignal(SignalName.DarknessChanged, isDark);
	}

	/// <summary>Required to generate a playable floor: it records and replays this floor's item deltas.</summary>
	[Export] public LevelItemPersistence ItemPersistence { get; set; }
	/// <summary>The floor's exploration and fog. Unassigned, fog is disabled and everything shows.</summary>
	[Export] public FloorDiscovery Discovery { get; set; }
	private Node _runtimeWorldRoot;
	public RealmEncounterController Encounters { get; private set; }
	public bool CanDescend => Encounters?.DescentUnlocked != false;

	public void AddWorldNode(Node node)
	{
		// Runtime spawns live under one root so regeneration can clear them without
		// touching authored children such as MapGenerator, cameras, or UI hooks.
		_runtimeWorldRoot ??= GetOrCreateRuntimeWorldRoot();
		_runtimeWorldRoot.AddChild(node);
	}

	public void AddWorldNode(Node3D node, Vector3 globalPosition)
	{
		AddWorldNode(node);
		node.GlobalPosition = globalPosition;
		node.ResetPhysicsInterpolation();
	}

	public override void _Ready()
	{
		if (MapGenerator == null)
		{
			GD.PushError($"{Name}: assign the level's MapGenerator.");
			return;
		}
		if (Engine.IsEditorHint())
		{
			return;
		}

		if (Discovery == null)
		{
			GD.PushError($"{Name}: assign the level's FloorDiscovery; fog is disabled.");
		}
		Run ??= new RunState(RunState.DefaultSeed);

		if (LightingEnvironment?.Environment != null)
		{
			// Own the runtime copy so a debug override cannot mutate the shared scene definition.
			_lightingEnvironment = (Environment)LightingEnvironment.Environment.Duplicate();
			_normalAmbientEnergy = _lightingEnvironment.AmbientLightEnergy;
			LightingEnvironment.Environment = _lightingEnvironment;
			_lightingEnvironment.AmbientLightEnergy = IsDark ? 0f : _normalAmbientEnergy;
		}
		_runtimeWorldRoot = GetOrCreateRuntimeWorldRoot();
		if (AutoGenerateOnReady)
		{
			Generate();
		}
	}

	/// <summary>
	/// Sets up the next generation: the run this floor belongs to, its depth, and the saved deltas to replay onto it.
	/// The level seed derives from the run seed and depth. Nothing under <see cref="Generate"/> reads the session.
	/// </summary>
	public void ConfigureGeneration(RunState run, uint depth, LevelItemDeltaSaveData savedItems, RevealedLevelSaveData savedExploration)
	{
		Run = run;
		MapGenerator.Seed = RunState.GetLevelSeed(run.RunSeed, depth);
		MapGenerator.DungeonDepth = depth;
		_savedItems = savedItems;
		_savedExploration = savedExploration;
	}

	/// <summary>The run this floor belongs to. A level never configured (previews, harnesses, tests) plays the default run.</summary>
	public RunState Run { get; private set; }
	private LevelItemDeltaSaveData _savedItems;
	private RevealedLevelSaveData _savedExploration;

	/// <summary>This floor's level seed. The generator export is its single store: configured here, or authored for previews and tests.</summary>
	public ulong Seed => MapGenerator.Seed;
	public uint Depth => MapGenerator.DungeonDepth;
	/// <summary>The generated floor gameplay queries; null until generation succeeds.</summary>
	public GeneratedFloor Floor { get; private set; }

	/// <summary>Recreates a reward stream without advancing generation or gameplay RNGs.</summary>
	public RandomNumberGenerator CreateSourceLootRng(string sourceId) => MapGenerator.CreateSourceLootRng(sourceId);

	/// <summary>True while an item operation is mid-commit; capturing then would record a half-applied floor.</summary>
	public bool IsCommittingFloorChanges => ItemPersistence?.IsCommitting == true;

	/// <summary>This floor's deviations from its seeded baseline; the inverse of <see cref="RestoreFloorDeltas"/>.</summary>
	public (LevelItemDeltaSaveData Items, RevealedLevelSaveData Exploration) CaptureFloorDeltas() =>
		(ItemPersistence.Capture(), CaptureExploration());

	/// <summary>This floor's door, trap and exploration deviations from its seeded baseline; the inverse of <see cref="RestoreExploration"/>.</summary>
	public RevealedLevelSaveData CaptureExploration()
	{
		if (Floor == null) throw new System.InvalidOperationException("No generated floor to capture.");
		return new RevealedLevelSaveData
		{
			DungeonDepth = Depth,
			Doors = Floor.DoorBaselines.Values.Where(pair => IsInstanceValid(pair.Door))
				.Select(pair => (Current: pair.Door.CaptureAccess(), pair.Baseline))
				.Where(pair => pair.Current.IsDiscovered != pair.Baseline.IsDiscovered || pair.Current.IsLocked != pair.Baseline.IsLocked
					|| pair.Current.IsOpen != pair.Baseline.IsOpen)
				.Select(pair => pair.Current).ToList(),
			ExploredSamples = Discovery?.CaptureSamples() ?? [],
			DiscoveredTrapIds = Floor.Traps.Where(trap => IsInstanceValid(trap) && trap.IsDiscovered).Select(trap => trap.PersistentId)
				.OrderBy(id => id, System.StringComparer.Ordinal).ToList(),
		};
	}

	/// <summary>Replays saved exploration onto the freshly generated floor. Validates everything before changing anything;
	/// door seals follow the restored doors on their own.</summary>
	public void RestoreExploration(RevealedLevelSaveData saved)
	{
		if (saved == null) return;
		if (saved.DungeonDepth != Depth) throw new System.InvalidOperationException("Wrong exploration floor.");
		var traps = Floor.Traps.Where(IsInstanceValid).ToDictionary(trap => trap.PersistentId);
		if (saved.DiscoveredTrapIds.Distinct().Count() != saved.DiscoveredTrapIds.Count || saved.DiscoveredTrapIds.Any(id => !traps.ContainsKey(id)))
			throw new System.InvalidOperationException("Unknown or duplicate saved trap discovery.");
		var ids = new HashSet<string>();
		foreach (var state in saved.Doors)
		{
			state.Validate();
			if (!ids.Add(state.ConnectionId) || !Floor.DoorBaselines.TryGetValue(state.ConnectionId, out var pair) || !IsInstanceValid(pair.Door)
				|| state.IsLocked && pair.Door.Lock?.Definition == null || !state.IsDiscovered && pair.Door.Secret?.IsSecret != true)
				throw new System.InvalidOperationException("Unknown, duplicate or incompatible saved door.");
		}
		// Validated against the floor even without a discovery node, so a bad record never survives a save round trip.
		DungeonDiscovery.ValidateSamples(Floor.Map, saved.ExploredSamples);
		foreach (var state in saved.Doors) Floor.DoorBaselines[state.ConnectionId].Door.RestoreAccess(state);
		foreach (var id in saved.DiscoveredTrapIds) traps[id].RestoreDiscovered(true);
		Discovery?.RestoreSamples(saved.ExploredSamples);
	}

	/// <summary>Replays the session's saved deltas for this depth onto the freshly generated baseline.</summary>
	private void RestoreFloorDeltas()
	{
		ItemPersistence.RecordInitialBaseline();
		ItemPersistence.Apply(_savedItems);
		RestoreExploration(_savedExploration);
	}

	public void Generate()
	{
		if (ItemPersistence == null)
		{
			GD.PushError($"{Name}: assign ItemPersistence before generating a playable floor.");
			return;
		}

		GD.Print("Initializing level...");
		// Clear previous runtime spawns before rebuilding the map. This matters if a
		// level is regenerated in place rather than replaced by a full scene reload.
		ClearWorldNodes();
		Floor = null;
		Discovery?.Clear();
		try { MapGenerator.GenerateMap(); }
		catch (System.InvalidOperationException error)
		{
			GD.PushError($"Cannot generate playable floor: {error.Message}");
			EmitSignal(SignalName.GenerationFailed, error.Message);
			return;
		}
		Floor = MapGenerator.Floor;
		Discovery?.Initialize(Floor);
		ItemPersistence.Initialize(this);
		try
		{
			RestoreFloorDeltas();
		}
		catch (System.Exception error)
		{
			GD.PushError($"Cannot restore floor items: {error.Message}");
			EmitSignal(SignalName.GenerationFailed, error.Message);
			return;
		}

		// Lives under RuntimeWorld so it is cleared and rebuilt with each generation.
		// Exploration polls the player independently of room-entry notifications.
		var roomManager = new RoomManager { Name = "RoomManager" };
		AddWorldNode(roomManager);
		roomManager.Initialize(Floor);

		Encounters = null;
		if (Floor.Encounter != null)
		{
			Encounters = new RealmEncounterController { Name = "Encounters" };
			AddWorldNode(Encounters);
			Encounters.Initialize(this, Floor.Encounter);
		}

		GD.Print("Level is ready");
		SignalBus.EmitLevelLoaded(this);
	}

	private Node GetOrCreateRuntimeWorldRoot()
	{
		Node root = GetNodeOrNull("RuntimeWorld");
		if (root != null)
		{
			return root;
		}

		// Create this at runtime only; the Level script is a tool script and should not
		// mutate the authored scene just because it was opened in the editor.
		root = new Node { Name = "RuntimeWorld" };
		AddChild(root);
		return root;
	}

	private void ClearWorldNodes()
	{
		_runtimeWorldRoot ??= GetOrCreateRuntimeWorldRoot();
		foreach (Node node in _runtimeWorldRoot.GetChildren())
		{
			// Detach first so group queries and signal listeners stop seeing stale nodes
			// immediately, even though QueueFree runs at the end of the frame.
			_runtimeWorldRoot.RemoveChild(node);
			node.QueueFree();
		}
	}
}