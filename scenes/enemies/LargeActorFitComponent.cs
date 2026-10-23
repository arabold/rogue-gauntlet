using Godot;

/// <summary>
/// Keeps an oversized room encounter out of narrow passages and hides its tall art above unrevealed fog.
/// Scene order places this after AI steering and before movement; ordinary enemies do not use it.
/// </summary>
public partial class LargeActorFitComponent : Node
{
	[Export] public EnemyBase Actor { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public LargeActorFitProfile Profile { get; set; }
	public float MovementClearance { get; private set; }
	public int HomeRoomId { get; private set; } = -1;
	private MapGenerator _map;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Clearance;
		_map = this.GetAncestorOrNull<Level>()?.MapGenerator;
		MovementClearance = Profile.MinimumClearance;
		if (_map != null)
		{
			HomeRoomId = _map.GetRoomIdAt(_map.WorldToTile(Actor.GlobalPosition));
		}
	}

	public bool CanMoveTo(Vector3 position) => _map == null || _map.IsEncounterFootprintClear(position, MovementClearance, HomeRoomId);

	public override void _PhysicsProcess(double delta)
	{
		if (_map == null || Actor.IsDead || Movement == null || Movement.LocomotionDirection.IsZeroApprox() || Movement.Speed <= 0) return;
		var next = Actor.GlobalPosition + Movement.LocomotionDirection * Movement.LocomotionSpeed * (float)delta;
		if (!CanMoveTo(next)) Movement.HaltLocomotion();
	}

	public override void _Process(double delta)
	{
		// Fog caps are four units high; this actor is taller and must not peek over them.
		if (_map != null) Actor.Visible = _map.IsRevealedAt(Actor.GlobalPosition);
	}
}