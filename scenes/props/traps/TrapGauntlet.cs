using Godot;
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
/// wakes its hazards only while the hero is within <see cref="ActivationRadius"/> of their danger and their space is
/// explored; asleep, they never fire or cut. Its hazards are all in place when it enters the tree.
/// </summary>
[GlobalClass]
public partial class TrapGauntlet : Node3D
{
	/// <summary>The hero's walking speed, which waves are timed for when no hero is at hand (generation). It is derived
	/// from the hero's stats, not tuned here.</summary>
	public const float HeroWalkSpeed = 6f;

	/// <summary>Horizontal distance from the hero to the hazards' danger at which the gauntlet wakes; more than the
	/// camera shows, so asleep hazards are out of view.</summary>
	[Export(PropertyHint.Range, "4,40,0.5,suffix:m")] public float ActivationRadius { get; set; } = 16f;

	/// <summary>Seconds of physics time since the gauntlet entered the tree.</summary>
	public double Clock { get; private set; }
	public bool IsAwake { get; private set; }

	private TrapHazard[] _hazards = [];
	/// <summary>The union of the hazards' danger, measured on the first tick, once physics knows the walls.</summary>
	private Aabb? _danger;
	/// <summary>Explored space never fogs over again, so once revealed the gauntlet stops asking.</summary>
	private bool _revealed;
	private ExplorationVisibility _exploration;
	private PlayerLookup _hero;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Damage;
		_exploration = new ExplorationVisibility(this);
		_hazards = GetChildren().OfType<TrapHazard>().ToArray();
		SetPhysicsProcess(_hazards.Length > 0);
	}

	public override void _PhysicsProcess(double delta)
	{
		Clock += delta;
		var hero = WakingHero(delta);
		IsAwake = hero != null;
		foreach (var hazard in _hazards) hazard.Sync(Clock, hero);
	}

	/// <summary>
	/// The phase of the <paramref name="row"/>-th row met along a path, <paramref name="distance"/> metres past the first
	/// row: Unison 0, Alternate half a period for every other row, Wave late by the walk there at <paramref name="walkSpeed"/>.
	/// </summary>
	public static float PhaseFor(HazardPattern pattern, int row, float distance, float period, float walkSpeed) => pattern switch
	{
		HazardPattern.Alternate => row % 2 * .5f,
		HazardPattern.Wave => Mathf.PosMod(-distance / walkSpeed / period, 1f),
		_ => 0f
	};

	/// <summary>The hero, while within <see cref="ActivationRadius"/> of the hazards' danger and that space is explored.</summary>
	private Node3D WakingHero(double delta)
	{
		var hero = _hero.Find(this, delta);
		if (hero == null) return null;
		var danger = _danger ??= _hazards.Skip(1).Aggregate(_hazards[0].DangerBounds, (all, hazard) => all.Merge(hazard.DangerBounds));
		Vector3 at = hero.GlobalPosition;
		if (at.HorizontalDistanceTo(at.Clamp(danger.Position, danger.End)) > ActivationRadius) return null;
		_revealed = _revealed || _hazards.Any(hazard => _exploration.IsRevealed(hazard.DangerBounds.GetCenter()));
		return _revealed ? hero : null;
	}
}
