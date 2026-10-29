using Godot;

/// <summary>
/// Evenly spread hail landing points: an R2 low-discrepancy sequence over the storm disc, so every spot sees a
/// similar shard rate instead of random clumps, offset once per cast from the caller's random stream.
/// </summary>
public sealed class HailPattern
{
	// Plastic-number steps of the R2 sequence, which fills the unit square more evenly than random samples.
	private const float StepX = .75487766f, StepY = .56984029f;
	private readonly float _offsetX, _offsetY, _shardsPerSecond;
	private float _budget;
	private int _index;

	public HailPattern(RandomNumberGenerator random, float shardsPerSecond)
	{
		_offsetX = random.Randf(); _offsetY = random.Randf(); _shardsPerSecond = shardsPerSecond;
	}
	/// <summary>Advances the storm clock and returns how many shards are due to drop.</summary>
	public int Advance(float delta)
	{
		_budget += delta * _shardsPerSecond;
		int due = (int)_budget; _budget -= due;
		return due;
	}
	/// <summary>The next landing point on the floor disc around <paramref name="origin"/>.</summary>
	public Vector3 Next(Vector3 origin, float radius)
	{
		_index++;
		float distance = radius * Mathf.Sqrt(Mathf.PosMod(_offsetX + _index * StepX, 1f));
		float angle = Mathf.Tau * Mathf.PosMod(_offsetY + _index * StepY, 1f);
		return origin + new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
	}
}