using Godot;

/// <summary>
/// A concealed floor plate, on while the hero stands on it, that reports each change to its <see cref="Output"/>. It
/// is found like a secret door, by proximity rolls from explored space, or by stepping on it, which reveals it without
/// the discovery pulse; discovery warns but never disarms it. What it sets off is up to its output's receivers: the
/// spikes of a spike trap, a wall launcher across the floor.
/// </summary>
[GlobalClass]
public partial class PressurePlate : Node3D, IProximityDiscoverable
{
	/// <summary>Detects bodies on the plate; its collision mask decides who presses it (only the hero, for a trap).</summary>
	[Export] public TriggerComponent Trigger { get; set; }
	[Export] public TriggerOutputComponent Output { get; set; }
	[Export] public MeshInstance3D FloorSurface { get; set; }
	/// <summary>How far the plate's top stands above the floor it lies in: a hair for a tile that replaces the floor's,
	/// more for a slab that should read as one once found.</summary>
	[Export(PropertyHint.Range, "0,0.2,0.005,suffix:m")] public float SurfaceLift { get; set; } = .025f;
	[Export] public DiscoveryPulse DiscoveryEffect { get; set; }
	/// <summary>Optional: without it the plate is found only by stepping on it.</summary>
	[Export] public ProximityDiscoveryComponent Discovery { get; set; }

	/// <summary>Stable identity for persisted discovery; generated content assigns it through <see cref="Initialize"/>.</summary>
	public string PersistentId { get; private set; } = "";
	public bool IsDiscovered { get; private set; }
	public bool IsPressed => Output?.IsOn == true;

	private GridMap _surfaceGrid;
	private Vector3I _surfaceCell;
	private ulong _levelSeed = RunState.DefaultSeed;
	private bool _initialized;
	private ExplorationVisibility _exploration;
	private ulong DiscoverySeed => GenerationRandom.DeriveSeed(_levelSeed, "trap-discovery", PersistentId);

	public override void _Ready()
	{
		if (Trigger == null || Output == null || FloorSurface == null)
		{
			GD.PushError($"{Name}: a pressure plate requires assigned Trigger, Output and FloorSurface nodes.");
			return;
		}
		this.SubscribeUntilExit(Trigger, t => t.Triggered += OnEntered, t => t.Triggered -= OnEntered);
		this.SubscribeUntilExit(Trigger, t => t.Released += OnLeft, t => t.Released -= OnLeft);
		_exploration = new ExplorationVisibility(this);
		if (!_initialized) PersistentId = "authored:" + GetPath();
		Discovery?.Initialize(DiscoverySeed);
		RestoreDiscovered(false);
	}

	/// <summary>Applies generation context: stable identity, the floor seed for discovery rolls and the floor cell the
	/// plate conceals. Call it once the plate is in place, since it aligns to its final height.</summary>
	public void Initialize(string persistentId, ulong levelSeed, GridMap surfaceGrid, Vector3I surfaceCell)
	{
		PersistentId = persistentId;
		_levelSeed = levelSeed;
		_surfaceGrid = surfaceGrid;
		_surfaceCell = surfaceCell;
		_initialized = true;
		if (!IsNodeReady()) return;
		Discovery?.Initialize(DiscoverySeed);
		AlignSurface();
	}

	private void AlignSurface()
	{
		if (_surfaceGrid == null) return;
		int item = _surfaceGrid.GetCellItem(_surfaceCell);
		if (item < 0) return;
		var transform = _surfaceGrid.GlobalTransform * _surfaceGrid.GetCellTransform(_surfaceCell) * _surfaceGrid.MeshLibrary.GetItemMeshTransform(item);
		float height = (transform * _surfaceGrid.MeshLibrary.GetItemMesh(item).GetAabb()).End.Y;
		FloorSurface.Position = Vector3.Up * (height - GlobalPosition.Y - FloorSurface.Mesh.GetAabb().End.Y + SurfaceLift);
	}

	public bool Reveal(bool playEffect = true)
	{
		if (IsDiscovered) return false;
		var original = playEffect && DiscoveryEffect != null && _surfaceGrid != null ? DiscoveryEffect.CaptureCell(_surfaceGrid, _surfaceCell, 0.05f) : null;
		RestoreDiscovered(true);
		if (playEffect) DiscoveryEffect?.Play(FloorSurface, original);
		return true;
	}

	public void RestoreDiscovered(bool discovered)
	{
		DiscoveryEffect?.Stop();
		AlignSurface();
		IsDiscovered = discovered;
		Visible = discovered;
	}

	/// <summary>Discovery requires explored space; neither proximity nor effects reveal a hidden room through a wall.</summary>
	public bool IsDiscoverable => !IsDiscovered && _exploration?.IsRevealed(GlobalPosition) == true;
	public Vector3 DiscoveryAnchor => GlobalPosition;
	public Vector3 SightTarget => GlobalPosition + Vector3.Up;

	public void Discover()
	{
		if (Reveal()) SignalBus.EmitPlayerStatusMessage("You spotted a trap!", LogSeverity.Warning);
	}

	private void OnEntered(Node3D body)
	{
		Reveal(false); // Stepping on it exposes the plate; what it sets off supplies the feedback.
		Output.Report(true, body);
	}

	private void OnLeft(Node3D body) => Output.Report(false, body);
}