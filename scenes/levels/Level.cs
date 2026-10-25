using Godot;

[Tool]
public partial class Level : Node
{
	[Export] public bool AutoGenerateOnReady { get; set; } = true;

	/// <summary>Optional scene environment whose ambient illumination follows this floor's darkness.</summary>
	[Export] public WorldEnvironment LightingEnvironment { get; set; }
	[Signal] public delegate void DarknessChangedEventHandler(bool isDark);
	/// <summary>Current floor illumination override; debug changes last for this level instance only.</summary>
	public bool IsDark { get; private set; }
	private float _normalAmbientEnergy;
	private Environment _lightingEnvironment;

	/// <summary>Extinguishes environmental fixtures and ambient light without changing exploration or player lights.</summary>
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

	public MapGenerator MapGenerator { get; private set; }
	public LevelItemPersistence ItemPersistence { get; private set; }
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
	}

	public override void _Ready()
	{
		MapGenerator = GetNode<MapGenerator>("MapGenerator");
		if (Engine.IsEditorHint())
		{
			return;
		}

		if (LightingEnvironment?.Environment != null)
		{
			// Own the runtime copy so a debug override cannot mutate the shared scene definition.
			_lightingEnvironment = (Environment)LightingEnvironment.Environment.Duplicate();
			_normalAmbientEnergy = _lightingEnvironment.AmbientLightEnergy;
			LightingEnvironment.Environment = _lightingEnvironment;
			_lightingEnvironment.AmbientLightEnergy = IsDark ? 0f : _normalAmbientEnergy;
		}
		_runtimeWorldRoot = GetOrCreateRuntimeWorldRoot();
		ItemPersistence = GetNodeOrNull<LevelItemPersistence>("ItemPersistence");
		if (ItemPersistence == null)
		{
			ItemPersistence = new LevelItemPersistence { Name = "ItemPersistence" };
			AddChild(ItemPersistence);
		}

		if (AutoGenerateOnReady)
		{
			Generate();
		}
	}

	public void ConfigureGeneration(ulong seed, uint dungeonDepth)
	{
		MapGenerator.Seed = seed;
		MapGenerator.DungeonDepth = dungeonDepth;
	}

	public void Generate()
	{
		GD.Print("Initializing level...");
		// Clear previous runtime spawns before rebuilding the map. This matters if a
		// level is regenerated in place rather than replaced by a full scene reload.
		ClearWorldNodes();
		try { MapGenerator.GenerateMap(); }
		catch (System.InvalidOperationException error)
		{
			GD.PushError($"Cannot generate playable floor: {error.Message}");
			GameSession.Instance?.CallDeferred(nameof(GameSession.ReturnToMenu));
			return;
		}
		ItemPersistence.Initialize(this);
		try
		{
			ItemPersistence.RecordInitialBaseline();
			ItemPersistence.Apply(GameSession.Instance?.FindLevelItemChanges(MapGenerator.DungeonDepth));
		}
		catch (System.Exception error)
		{
			GD.PushError($"Cannot restore floor items: {error.Message}");
			GameSession.Instance?.CallDeferred(nameof(GameSession.ReturnToMenu));
			return;
		}

		// Live under RuntimeWorld so they are cleared and rebuilt with each
		// generation. FogOfWar must subscribe before RoomManager's first poll, so
		// create it first.
		var fogOfWar = new FogOfWar { Name = "FogOfWar" };
		AddWorldNode(fogOfWar);
		fogOfWar.Initialize(MapGenerator);

		var roomManager = new RoomManager { Name = "RoomManager" };
		AddWorldNode(roomManager);
		roomManager.Initialize(MapGenerator);

		Encounters = null;
		if (MapGenerator.MobFactory is RealmEncounterFactory definition && definition.ContainsDepth(MapGenerator.DungeonDepth))
		{
			Encounters = new RealmEncounterController { Name = "Encounters" };
			AddWorldNode(Encounters);
			Encounters.Initialize(this, definition);
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