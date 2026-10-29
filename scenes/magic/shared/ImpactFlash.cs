using Godot;

/// <summary>
/// Rate-limits bright impact flashes across all spell effects, so dense showers and storms never
/// strobe faster than about three times a second (the common photosensitivity guideline). Claims
/// within a few frames of a granted flash share it, since simultaneous hits read as one flash.
/// Effects refused a flash still play at their normal brightness.
/// </summary>
public static class ImpactFlash
{
	private const ulong MinIntervalMsec = 334;
	private const ulong SharedWindowMsec = 50;
	private static ulong _windowStart;
	private static bool _granted;

	public static bool TryClaim()
	{
		ulong now = Time.GetTicksMsec();
		if (_granted && now - _windowStart < SharedWindowMsec) return true;
		if (_granted && now - _windowStart < MinIntervalMsec) return false;
		_windowStart = now;
		_granted = true;
		return true;
	}
}
