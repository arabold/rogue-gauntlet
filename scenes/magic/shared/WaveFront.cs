using Godot;

/// <summary>
/// The outward front a wave visual draws over its timeline, as fractions of the gameplay radius. Its timeline feeds
/// it to the wave shaders as <c>wave_front</c> and area contacts follow it, so the drawn wave and the hits stay in
/// step. Each shader maps the fraction onto its own mesh with its <c>wave_edge</c> uniform.
/// </summary>
[GlobalClass]
public partial class WaveFront : Resource
{
	[Export] public float Start { get; set; }
	[Export] public float End { get; set; } = 1;
	[Export] public float Power { get; set; } = 2;

	/// <summary>Where the drawn front is at timeline <paramref name="progress"/>.</summary>
	public float At(float progress) => new AreaFront(0, 1, Start, End, Power).Travel(progress);

	/// <summary>The front <paramref name="front"/> draws over <paramref name="timeline"/>; none while either is unassigned.</summary>
	public static AreaFront Of(WaveFront front, VisualEffectTimeline timeline) =>
		front != null && timeline != null ? new(0, timeline.Duration, front.Start, front.End, front.Power) : AreaFront.Instant;
}
