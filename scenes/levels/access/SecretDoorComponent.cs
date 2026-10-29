using Godot;
using System;

/// <summary>Conceals a real wall tile until proximity discovery or an explicit mechanism reveals it.
/// Owners observe <see cref="Changed"/> instead of being refreshed by each mutation site.</summary>
[Tool]
[GlobalClass]
public partial class SecretDoorComponent : Node, IProximityDiscoverable
{
	[Signal] public delegate void ChangedEventHandler();
	[Export] public Door Door { get; set; }
	[Export] public DiscoveryPulse DiscoveryEffect { get; set; }
	/// <summary>Optional: without it the door is found only through explicit mechanisms.</summary>
	[Export] public ProximityDiscoveryComponent Discovery { get; set; }
	public bool IsSecret { get; private set; }
	public bool IsDiscovered { get; private set; } = true;
	private GridMap _walls;
	private Vector3I _cell;
	private int _item, _orientation;
	private Aabb _wallBounds;
	private Vector3 _approach;
	public void Initialize(GridMap walls, Vector3I cell, Vector3 approach, SecretDetectionProfile profile, ulong seed)
	{
		_walls = walls; _cell = cell; _approach = approach;
		_item = walls.GetCellItem(cell); _orientation = walls.GetCellItemOrientation(cell);
		if (_item < 0) throw new InvalidOperationException("A concealed door requires an ordinary wall tile.");
		_wallBounds = walls.GlobalTransform * walls.GetCellTransform(cell) * walls.MeshLibrary.GetItemMeshTransform(_item)
			* walls.MeshLibrary.GetItemMesh(_item).GetAabb();
		Discovery?.Initialize(seed, profile);
		IsSecret = true; Restore(false);
	}
	public bool Reveal()
	{
		if (IsDiscovered) return false;
		var original = DiscoveryEffect?.CaptureCell(_walls, _cell);
		Restore(true);
		DiscoveryEffect?.Play(Door.DoorVisual, original);
		return true;
	}
	public void Restore(bool discovered)
	{
		if (!IsSecret && !discovered) throw new InvalidOperationException("Ordinary door cannot be concealed.");
		DiscoveryEffect?.Stop();
		IsDiscovered = discovered;
		if (_walls != null) _walls.SetCellItem(_cell, discovered ? -1 : _item, _orientation);
		EmitSignal(SignalName.Changed);
	}
	public bool IsDiscoverable => IsSecret && !IsDiscovered && !Engine.IsEditorHint();
	public Vector3 DiscoveryAnchor => Door.GlobalPosition;
	public Vector3 SightTarget => Door.GlobalPosition + Vector3.Up * 2;
	public bool CanBeObservedFrom(Vector3 observer) => (observer - Door.GlobalPosition).Dot(_approach) > 0;
	/// <summary>Any wall variant may conceal a door, including protruding/thick tiles, so the concealing wall itself counts as seen.</summary>
	public bool AcceptsSightHit(GodotObject collider, Vector3 point) => _wallBounds.Grow(0.05f).HasPoint(point)
		&& (collider == _walls || collider is Node node && Door.IsAncestorOf(node));
	public void Discover() => Reveal();
}