using Godot;

/// <summary>
/// A visible, rhythmic trap in a <see cref="TrapGauntlet"/>. It never keeps its own time: the gauntlet syncs every
/// member from one clock, so hazards placed together stay in step.
/// </summary>
[Tool, GlobalClass]
public abstract partial class TrapHazard : Trap
{
	/// <summary>Seconds per cycle.</summary>
	[Export(PropertyHint.Range, "0.7,10,0.05,suffix:s")] public float Period { get; set; } = 2f;
	/// <summary>Offset into the cycle, 0 to 1; a pattern staggers its rows through it.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float Phase { get; set; }

	/// <summary>World-space box the hazard can hurt in, fixed once it stands in place: where it wakes from, and what
	/// generation keeps clear.</summary>
	public abstract Aabb DangerBounds { get; }

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
}