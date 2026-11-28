using Godot;

/// <summary>
/// Rate-limits bright impact flashes across all spell effects, so dense showers and storms never
/// strobe faster than about three times a second (the common photosensitivity guideline). Claims
/// within a few frames of a granted flash share it, since simultaneous hits read as one flash.
/// Effects refused a flash still play at their normal brightness.
/// </summary>
public static class ImpactFlash
{
	private const double MinInterval = .334;
	private const double SharedWindow = .05;
	private static double _windowStart;
	private static bool _granted;

	public static bool TryClaim()
	{
		double now = FrameClock.Seconds;
		if (_granted && now - _windowStart < SharedWindow) return true;
		if (_granted && now - _windowStart < MinInterval) return false;
		_windowStart = now;
		_granted = true;
		return true;
	}
}
