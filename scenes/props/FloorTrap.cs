using Godot;
using System;

/// <summary>A concealed, reusable spike trap. Discovery warns the player but never disarms it.</summary>
public partial class FloorTrap : Node3D, IProximityDiscoverable
{
	[Export] public int Damage { get; set; } = 5;
	[Export] public TriggerComponent Trigger { get; set; }
	[Export] public MeshInstance3D Spikes { get; set; }
	[Export] public DiscoveryPulse DiscoveryEffect { get; set; }
	[Export] public MeshInstance3D FloorSurface { get; set; }
	/// <summary>Optional: without it the trap is found only by stepping on it.</summary>
	[Export] public ProximityDiscoveryComponent Discovery { get; set; }

	/// <summary>Stable identity for persisted discovery; generated content assigns it through <see cref="Initialize"/>.</summary>
	public string PersistentId { get; private set; } = "";
	public bool IsDiscovered { get; private set; }
	public bool IsTriggered { get; private set; }
	private GridMap _surfaceGrid;
	private Vector3I _surfaceCell;
	private ulong _levelSeed = 42;
	private bool _initialized;
	private ExplorationVisibility _exploration;
	private ulong DiscoverySeed => GenerationRandom.DeriveSeed(_levelSeed, "trap-discovery", PersistentId);
	public override void _Ready()
	{
		if (Trigger == null || Spikes == null || FloorSurface == null)
		{
			GD.PushError($"{Name}: a floor trap requires assigned Trigger, Spikes and FloorSurface nodes.");
			return;
		}
		Trigger.Triggered += OnTriggered;
		Spikes.Position = new Vector3(0, -2, 0);
		_exploration = new ExplorationVisibility(this);
		if (!_initialized) PersistentId = "authored:" + GetPath();
		Discovery?.Initialize(DiscoverySeed);
		RestoreDiscovered(false);
	}
	/// <summary>Applies generation context: stable identity, the floor seed for discovery rolls and the floor cell the
	/// plate conceals. Call it once the trap is in place, since the plate aligns to its final height.</summary>
	public void Initialize(string persistentId, ulong levelSeed, GridMap surfaceGrid, Vector3I surfaceCell)
	{
		PersistentId = persistentId; _levelSeed = levelSeed; _surfaceGrid = surfaceGrid; _surfaceCell = surfaceCell;
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
		FloorSurface.Position = Vector3.Up * (height - GlobalPosition.Y - FloorSurface.Mesh.GetAabb().End.Y + 0.025f);
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
	public void Discover() => Reveal();
	private void OnTriggered(Node3D node)
	{
		if (IsTriggered || node is not IDamageable damageable) return;
		IsTriggered = true;
		DiscoveryEffect?.Stop();
		Reveal(false); // Stepping on it exposes the trap; its spike attack supplies the feedback.
		damageable.TakeDamage(Damage, node.Basis.Z, this);
		var tween = CreateTween();
		tween.TweenProperty(Spikes, "position:y", 0f, 0.2f);
		tween.TweenProperty(Spikes, "position:y", -2f, 1.5f).SetDelay(0.5f);
		tween.Finished += () => IsTriggered = false;
	}
}