using Godot;

/// <summary>
/// How far an area spell's visible effect has travelled from its origin, so its contacts land when the effect
/// reaches each actor rather than at release. After <see cref="Delay"/> the front eases out from <see cref="Start"/>
/// to <see cref="End"/> (fractions of the gameplay radius) over <see cref="Seconds"/>:
/// <c>lerp(Start, End, 1 - (1 - t)^Power)</c>. Once it finishes, it has covered the whole area.
/// </summary>
public readonly record struct AreaFront(float Delay, float Seconds, float Start, float End, float Power)
{
	/// <summary>No travel: the whole area is reached at release.</summary>
	public static readonly AreaFront Instant = new(0, 0, 1, 1, 1);

	/// <summary>Fraction of the gameplay radius covered after <paramref name="elapsed"/> seconds; 1 once it covers the whole area.</summary>
	public float Reach(float elapsed) => elapsed >= Delay + Seconds ? 1 : Mathf.Min(Travel(elapsed), 1);

	/// <summary>Where the drawn front is after <paramref name="elapsed"/> seconds, which may lie past the gameplay radius.</summary>
	public float Travel(float elapsed)
	{
		if (elapsed < Delay) return 0;
		float t = Seconds > 0 ? Mathf.Min((elapsed - Delay) / Seconds, 1) : 1;
		return Mathf.Lerp(Start, End, 1 - Mathf.Pow(1 - t, Power));
	}

	/// <summary>Seconds until the front reaches <paramref name="reach"/> of the radius, so effects can strike as it passes.</summary>
	public float Arrival(float reach)
	{
		if (End <= Start) return Delay;
		float eased = Mathf.Clamp((reach - Start) / (End - Start), 0, 1);
		return Delay + Seconds * (1 - Mathf.Pow(1 - eased, 1 / Power));
	}
}
