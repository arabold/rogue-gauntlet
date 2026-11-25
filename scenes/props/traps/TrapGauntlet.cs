using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>How a gauntlet staggers its rows along the path through it.</summary>
public enum HazardPattern
{
	/// <summary>Every row together: one gap crosses everything.</summary>
	Unison,
	/// <summary>Every other row half a period later: the hero stops between rows.</summary>
	Alternate,
	/// <summary>Each row later by the walk from the first row: started right, one walk goes clean through.</summary>
	Wave
}

/// <summary>
/// One arrangement of trap hazards, its direct children, on a shared beat. The clock starts when the gauntlet enters the
/// tree, advances with physics and pauses with the game, so a floor replays the same beat on every load. The gauntlet
/// wakes its hazards only while the hero is within <see cref="ActivationRadius"/> of one of them and their space is
/// explored; asleep, they never fire or cut.
/// </summary>
[GlobalClass]
public partial class TrapGauntlet : Node3D
{
	private static readonly StringName PlayerGroup = "player";
	private const float PlayerLookupInterval = .5f;
	/// <summary>Hazards closer than this along the path form one row and share its beat.</summary>
	private const float RowTolerance = .5f;
	/// <summary>The hero's walking speed, which waves are timed for when no hero is at hand (generation). It is derived
	/// from the hero's stats, not tuned here.</summary>
	public const float HeroWalkSpeed = 6f;

	/// <summary>Horizontal distance from the hero to a hazard's danger at which the gauntlet wakes.</summary>
	[Export(PropertyHint.Range, "4,40,0.5,suffix:m")] public float ActivationRadius { get; set; } = 16f;

	[Signal] public delegate void AwakeChangedEventHandler(bool awake);

	/// <summary>Seconds of physics time since the gauntlet entered the tree.</summary>
	public double Clock { get; private set; }
	public bool IsAwake { get; private set; }
	public IReadOnlyList<ITrapHazard> Hazards => _hazards;

	private readonly List<ITrapHazard> _hazards = [];
	private ExplorationVisibility _exploration;
	private Node3D _player;
	private Cooldown _playerLookup;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Damage;
		_exploration = new ExplorationVisibility(this);
		ChildEnteredTree += OnChildEnteredTree;
		ChildExitingTree += OnChildExitingTree;
		foreach (Node child in GetChildren()) OnChildEnteredTree(child);
	}

	public override void _PhysicsProcess(double delta)
	{
		Clock += delta;
		bool awake = ShouldWake(delta);
		if (awake != IsAwake)
		{
			IsAwake = awake;
			EmitSignalAwakeChanged(awake);
		}
		foreach (var hazard in _hazards) hazard.Sync(Clock, awake);
	}

	/// <summary>
	/// Gives every hazard <paramref name="period"/> and staggers the rows met along <paramref name="path"/> (a horizontal
	/// direction in this node's space) by <paramref name="pattern"/>. Wave delays each row by the time a hero walking at
	/// <paramref name="walkSpeed"/> needs to reach it from the first row. Works before the gauntlet enters the tree.
	/// </summary>
	public void ApplyPattern(HazardPattern pattern, float period, Vector3 path, float walkSpeed)
	{
		path.Y = 0;
		if (period <= 0 || path.LengthSquared() < 1e-6f)
		{
			GD.PushError($"{Name}: a pattern needs a positive period and a path direction.");
			return;
		}
		path = path.Normalized();
		var ordered = GetChildren().OfType<Node3D>().OfType<ITrapHazard>()
			.Select(hazard => (Hazard: hazard, Along: ((Node3D)hazard).Position.Dot(path)))
			.OrderBy(entry => entry.Along).ToList();
		if (ordered.Count == 0) return;
		float first = ordered[0].Along, rowStart = float.NegativeInfinity;
		int row = -1;
		foreach (var (hazard, along) in ordered)
		{
			if (along - rowStart > RowTolerance) { row++; rowStart = along; }
			hazard.Tune(new HazardBeat(period, PhaseFor(pattern, row, rowStart - first, period, walkSpeed)));
		}
	}

	/// <summary>
	/// The phase of the <paramref name="row"/>-th row met along a path, <paramref name="distance"/> metres past the first
	/// row: Unison 0, Alternate half a period for every other row, Wave late by the walk there at <paramref name="walkSpeed"/>.
	/// </summary>
	public static float PhaseFor(HazardPattern pattern, int row, float distance, float period, float walkSpeed) => pattern switch
	{
		HazardPattern.Alternate => row % 2 * .5f,
		HazardPattern.Wave => Mathf.PosMod(-distance / Mathf.Max(walkSpeed, .1f) / period, 1f),
		_ => 0f
	};

	private bool ShouldWake(double delta)
	{
		var player = FindPlayer(delta);
		if (player == null) return false;
		Vector3 hero = player.GlobalPosition;
		bool near = false, revealed = false;
		foreach (var hazard in _hazards)
		{
			Aabb danger = hazard.DangerBounds;
			near |= HorizontalDistance(danger, hero) <= ActivationRadius;
			revealed |= _exploration.IsRevealed(danger.GetCenter());
			if (near && revealed) return true;
		}
		return false;
	}

	private static float HorizontalDistance(Aabb box, Vector3 point)
	{
		Vector3 end = box.End;
		float dx = Mathf.Max(Mathf.Max(box.Position.X - point.X, 0), point.X - end.X);
		float dz = Mathf.Max(Mathf.Max(box.Position.Z - point.Z, 0), point.Z - end.Z);
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	/// <summary>Caches the hero; a missing one is looked up again only every <see cref="PlayerLookupInterval"/>.</summary>
	private Node3D FindPlayer(double delta)
	{
		if (IsInstanceValid(_player) && _player.IsInsideTree()) return _player;
		if (!_playerLookup.Tick(delta)) return null;
		_playerLookup.Start(PlayerLookupInterval);
		return _player = GetTree().GetFirstNodeInGroup(PlayerGroup) as Node3D;
	}

	private void OnChildEnteredTree(Node child)
	{
		if (child is ITrapHazard hazard && !_hazards.Contains(hazard)) _hazards.Add(hazard);
	}

	private void OnChildExitingTree(Node child)
	{
		if (child is ITrapHazard hazard) _hazards.Remove(hazard);
	}
}
