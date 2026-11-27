using Godot;

/// <summary>
/// A visible, rhythmic trap in a <see cref="TrapGauntlet"/>. It never keeps its own time: the gauntlet syncs every
/// member from one clock, so hazards placed together stay in step. Hazards are in the <c>trap</c> group, so their hits
/// carry the trap faction, which monsters ignore. Every hazard is a tool script with an editor guard, because room
/// emission tunes hazards in editor previews too; they act only in a game.
/// </summary>
[Tool, GlobalClass]
public abstract partial class TrapHazard : Node3D
{
	private static readonly StringName TrapGroup = "trap";

	/// <summary>Damage on the first depth; the balance model derives it.</summary>
	[Export] public int Damage { get; set; } = 4;
	[Export] public float DamagePerDepth { get; set; }
	/// <summary>Seconds per cycle.</summary>
	[Export(PropertyHint.Range, "0.7,10,0.05,suffix:s")] public float Period { get; set; } = 2f;
	/// <summary>Offset into the cycle, 0 to 1; a pattern staggers its rows through it.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float Phase { get; set; }

	/// <summary>What a hit deals on its level's depth (the first outside a level).</summary>
	public int CurrentDamage => TrapDamage.At(Damage, DamagePerDepth, _level?.Depth ?? 1);

	/// <summary>World-space box the hazard can hurt in, fixed once it stands in place: where it wakes from, and what
	/// generation keeps clear.</summary>
	public abstract Aabb DangerBounds { get; }

	private Level _level;

	public override void _EnterTree()
	{
		if (Engine.IsEditorHint()) return;
		_level = this.GetAncestorOrNull<Level>();
		AddToGroup(TrapGroup);
	}

	/// <summary>Applies a pattern's or a room step's beat; call before the hazard enters the tree.</summary>
	public virtual void Tune(HazardBeat beat)
	{
		Period = beat.Period;
		Phase = beat.Phase;
	}

	/// <summary>
	/// Advances to the gauntlet's <paramref name="clock"/>. <paramref name="hero"/> is the hero the gauntlet woke for, null
	/// while it sleeps: an asleep hazard holds still and never fires, hits or warns.
	/// </summary>
	public abstract void Sync(double clock, Node3D hero);

	/// <summary>Cycles completed at <paramref name="clock"/>, counting the phase: the fraction is progress through the beat.</summary>
	protected double CycleAt(double clock) => clock / Period + Phase;

	/// <summary>A seed for this hazard's own stream in <paramref name="domain"/>, distinct per <paramref name="source"/>.</summary>
	protected ulong DeriveSeed(string domain, string source = "") =>
		GenerationRandom.DeriveSeed(_level?.Seed ?? RunState.DefaultSeed, domain, $"{GetPath()}{source}");
}
