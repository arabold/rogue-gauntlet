using Godot;

/// <summary>
/// A concealed floor plate, on while the hero stands on it, that reports each change to its <see cref="Output"/>. It
/// is found like a secret door, by proximity rolls from explored space, or by stepping on it, which reveals it without
/// the discovery pulse; discovery warns but never disarms it. A body passing over it without touching the floor (the hero
/// mid-dodge, <see cref="IFloorContact"/>) presses it only if it touches down while still on it. What it sets off is up to its output's receivers: the
/// spikes of a spike trap, a wall launcher across the floor. Its <see cref="Visual"/> never fits itself to the floor
/// beneath: it draws on whatever floor is there (a spike trap's holes) or rises out of it taller than any floor tile (a
/// launcher's slab), so the plate stands at the floor plane wherever it lies.
/// </summary>
[GlobalClass]
public partial class PressurePlate : Node3D, IProximityDiscoverable
{
	/// <summary>Detects bodies on the plate; its collision mask decides who presses it (only the hero, for a trap).</summary>
	[Export] public TriggerComponent Trigger { get; set; }
	[Export] public TriggerOutputComponent Output { get; set; }
	/// <summary>What a found plate shows, faded in by <see cref="DiscoveryEffect"/>: its holes, its slab.</summary>
	[Export] public Node3D Visual { get; set; }
	[Export] public DiscoveryPulse DiscoveryEffect { get; set; }
	/// <summary>Optional: without it the plate is found only by stepping on it.</summary>
	[Export] public ProximityDiscoveryComponent Discovery { get; set; }

	/// <summary>Stable identity for persisted discovery; generated content assigns it through <see cref="Initialize"/>.</summary>
	public string PersistentId { get; private set; } = "";
	public bool IsDiscovered { get; private set; }
	public bool IsPressed => Output?.IsOn == true;

	/// <summary>A body over the plate that has not touched it yet: pressed if it touches down before it leaves.</summary>
	private Node3D _passing;
	private GridMap _surfaceGrid;
	private Vector3I _surfaceCell;
	private ulong _levelSeed = RunState.DefaultSeed;
	private bool _initialized;
	private ExplorationVisibility _exploration;
	private ulong DiscoverySeed => GenerationRandom.DeriveSeed(_levelSeed, "trap-discovery", PersistentId);

	public override void _Ready()
	{
		if (Trigger == null || Output == null || Visual == null)
		{
			GD.PushError($"{Name}: a pressure plate requires assigned Trigger, Output and Visual nodes.");
			return;
		}
		this.SubscribeUntilExit(Trigger, t => t.Triggered += OnEntered, t => t.Triggered -= OnEntered);
		this.SubscribeUntilExit(Trigger, t => t.Released += OnLeft, t => t.Released -= OnLeft);
		_exploration = new ExplorationVisibility(this);
		SetPhysicsProcess(false);
		if (!_initialized) PersistentId = "authored:" + GetPath();
		Discovery?.Initialize(DiscoverySeed);
		RestoreDiscovered(false);
	}

	/// <summary>Applies generation context: stable identity, the floor seed for discovery rolls and the floor cell the
	/// plate conceals, which the discovery glow starts from.</summary>
	public void Initialize(string persistentId, ulong levelSeed, GridMap surfaceGrid, Vector3I surfaceCell)
	{
		PersistentId = persistentId;
		_levelSeed = levelSeed;
		_surfaceGrid = surfaceGrid;
		_surfaceCell = surfaceCell;
		_initialized = true;
		if (!IsNodeReady()) return;
		Discovery?.Initialize(DiscoverySeed);
	}

	public bool Reveal(bool playEffect = true)
	{
		if (IsDiscovered) return false;
		var original = playEffect && DiscoveryEffect != null && _surfaceGrid != null ? DiscoveryEffect.CaptureCell(_surfaceGrid, _surfaceCell, 0.05f) : null;
		RestoreDiscovered(true);
		if (playEffect) DiscoveryEffect?.Play(Visual, original);
		return true;
	}

	public void RestoreDiscovered(bool discovered)
	{
		DiscoveryEffect?.Stop();
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
		if (body is IFloorContact { TouchesFloor: false })
		{
			_passing = body;
			SetPhysicsProcess(true);
			return;
		}
		Press(body);
	}

	private void OnLeft(Node3D body)
	{
		if (body == _passing) StopWatching();
		Output.Report(false, body);
	}

	/// <summary>Watches a body passing over the plate until it touches down on it or leaves.</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (!IsInstanceValid(_passing)) StopWatching();
		else if (_passing is not IFloorContact { TouchesFloor: false })
		{
			var body = _passing;
			StopWatching();
			Press(body);
		}
	}

	private void StopWatching()
	{
		_passing = null;
		SetPhysicsProcess(false);
	}

	private void Press(Node3D body)
	{
		Reveal(false); // Stepping on it exposes the plate; what it sets off supplies the feedback.
		Output.Report(true, body);
	}
}