using Godot;
using System.Linq;

/// <summary>How a gauntlet staggers its rows along the path through it.</summary>
public enum HazardPattern
{
	/// <summary>Every row together: one gap crosses everything.</summary>
	Unison,
	/// <summary>Every other row half a period later: the hero stops between rows.</summary>
	Alternate,
	/// <summary>Each row a quarter beat after the one before: a sweep rolls along the gauntlet for the hero to follow.</summary>
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
	/// <summary>How far through the beat each row of a wave lags the row before it. Heroes walk at different speeds, and
	/// a gauntlet is crossed row by row, pausing between them, so no pattern is timed for a walk.</summary>
	private const float WaveLag = .25f;

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
	/// The phase of the <paramref name="row"/>-th row of a gauntlet: Unison 0, Alternate half a beat for every other row,
	/// Wave a quarter beat later for each row.
	/// </summary>
	public static float PhaseFor(HazardPattern pattern, int row) => pattern switch
	{
		HazardPattern.Alternate => row % 2 * .5f,
		HazardPattern.Wave => Mathf.PosMod(-row * WaveLag, 1f),
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
