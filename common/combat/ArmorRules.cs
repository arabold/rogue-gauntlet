/// <summary>
/// How armor softens a physical hit, as in Shattered Pixel Dungeon and Dungeon Crawl: it blocks a random amount between
/// zero and its value, but a hit of at least a minimum always deals that much, so armor never stops a hit outright. Each
/// hurtbox sets its minimum (<see cref="HurtBoxComponent.MinimumDamage"/>); the balance model mirrors the default
/// (<c>scripts/balance/params.py</c>, <c>ARMOR</c>) and <c>simulate.py audit</c> checks it.
/// </summary>
public static class ArmorRules
{
	/// <summary>
	/// The damage of a physical <paramref name="hit"/> that gets through <paramref name="armor"/>: at least
	/// <paramref name="minimum"/> (a smaller hit deals itself), rolling the block from <paramref name="random"/> (the global
	/// generator when null).
	/// </summary>
	public static float Mitigate(float hit, float armor, float minimum, Godot.RandomNumberGenerator random)
	{
		if (hit <= 0) return 0;
		if (armor <= 0) return hit;
		float block = random?.RandfRange(0, armor) ?? (float)Godot.GD.RandRange(0f, armor);
		return System.MathF.Max(System.MathF.Min(hit, minimum), hit - block);
	}
}