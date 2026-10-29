using System;

/// <summary>Fresh presses the input controller may hold until an action can accept them.</summary>
public enum PlayerIntent { Primary, Secondary, Dodge, GuardPress }

/// <summary>
/// Remembers each fresh press for <c>window</c> seconds of gameplay time, so a press made while the player is
/// still busy runs as soon as the current action allows it instead of being dropped. A press is consumed at
/// most once; a newer press of the same intent replaces the older one.
/// </summary>
public sealed class InputIntentBuffer(double window)
{
	private readonly double[] _pushedAt = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];

	public void Push(PlayerIntent intent, double now) => _pushedAt[(int)intent] = now;

	public bool IsPending(PlayerIntent intent, double now) => now - _pushedAt[(int)intent] <= window;

	public bool TryConsume(PlayerIntent intent, double now)
	{
		if (!IsPending(intent, now)) return false;
		_pushedAt[(int)intent] = double.NegativeInfinity;
		return true;
	}

	public void Clear() => Array.Fill(_pushedAt, double.NegativeInfinity);
}
