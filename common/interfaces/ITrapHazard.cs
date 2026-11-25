using Godot;

/// <summary>
/// A visible, rhythmic hazard in a <see cref="TrapGauntlet"/>. It never keeps its own time: the gauntlet syncs every
/// member from one clock, so hazards placed together stay in step and a woken hazard never shows a jump.
/// </summary>
public interface ITrapHazard
{
	/// <summary>Applies a pattern's or a room step's beat; call before the hazard enters the tree or between syncs.</summary>
	void Tune(HazardBeat beat);

	/// <summary>
	/// Advances to the gauntlet's <paramref name="clock"/>. Asleep, a hazard never fires, hits or telegraphs, though it may
	/// keep moving (a pendulum swings on); awake, it acts on the clock.
	/// </summary>
	void Sync(double clock, bool awake);

	/// <summary>World-space box the hazard can hurt in: where it wakes from, and what generation keeps clear.</summary>
	Aabb DangerBounds { get; }
}
