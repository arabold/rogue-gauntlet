using Godot;

/// <summary>Remembers a player's latest steering vector briefly so a released direction can complete a dash gesture.</summary>
public sealed class DodgeDirectionMemory
{
	private Vector3 _direction;
	private double _observedAt = double.NegativeInfinity;

	/// <summary>Refresh the remembered direction from valid, nonzero horizontal steering.</summary>
	public void Observe(Vector3 direction, double now)
	{
		if (!double.IsFinite(now) || !direction.TryNormalizeHorizontal(out Vector3 normalized)) return;
		_direction = normalized;
		_observedAt = now;
	}

	/// <summary>Resolve current steering, recent steering, then facing without changing the stored sample.</summary>
	public Vector3 Resolve(Vector3 currentDirection, Vector3 facing, double now, double memoryDuration)
	{
		if (currentDirection.TryNormalizeHorizontal(out Vector3 current)) return current;
		if (double.IsFinite(memoryDuration) && memoryDuration > 0 && double.IsFinite(now)
			&& now >= _observedAt && now - _observedAt < memoryDuration)
		{
			return _direction;
		}
		if (facing.TryNormalizeHorizontal(out Vector3 normalizedFacing)) return normalizedFacing;
		return Vector3.Forward;
	}

	/// <summary>Forget steering when focus, life, or controller ownership changes.</summary>
	public void Clear()
	{
		_direction = Vector3.Zero;
		_observedAt = double.NegativeInfinity;
	}

}