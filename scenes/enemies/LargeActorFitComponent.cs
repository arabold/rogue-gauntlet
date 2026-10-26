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
	private Level _level;
	private ExplorationVisibility _exploration;

	/// <summary>Outside a generated level (previews, harnesses) the actor moves and shows without floor limits.</summary>
	private GeneratedFloor Floor => _level?.Floor;

	public override void _EnterTree() => _level = this.GetAncestorOrNull<Level>();

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Clearance;
		MovementClearance = Profile.MovementClearance;
		_exploration = new ExplorationVisibility(this);
		if (Floor != null)
		{
			HomeRoomId = Floor.RoomIdAt(Floor.Map.WorldToTile(Actor.GlobalPosition));
		}
	}

	public bool CanMoveTo(Vector3 position) => Floor?.IsFootprintClear(position, MovementClearance, HomeRoomId) ?? true;

	public override void _PhysicsProcess(double delta)
	{
		if (Floor == null || Actor.IsDead || Movement == null || Movement.LocomotionDirection.IsZeroApprox() || Movement.Speed <= 0) return;
		var next = Actor.GlobalPosition + Movement.LocomotionDirection * Movement.LocomotionSpeed * (float)delta;
		if (!CanMoveTo(next)) Movement.HaltLocomotion();
	}

	public override void _Process(double delta)
	{
		// Fog caps are four units high; this actor is taller and must not peek over them.
		if (Floor == null) return;
		bool revealed = _exploration.IsRevealed(Actor.GlobalPosition);
		if (Actor.Visible != revealed) Actor.Visible = revealed;
	}
}