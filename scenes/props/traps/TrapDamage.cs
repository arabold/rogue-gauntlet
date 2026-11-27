using Godot;

/// <summary>Depth scaling every trap shares, so a trap keeps its share of the hero's growing health.</summary>
public static class TrapDamage
{
	/// <summary>
	/// <paramref name="damage"/> on the first depth plus <paramref name="perDepth"/> for each depth below it, at least 1.
	/// The balance model derives both (`simulate.py derive`).
	/// </summary>
	public static int At(int damage, float perDepth, uint depth) =>
		Mathf.Max(1, Mathf.RoundToInt(damage + perDepth * ((int)System.Math.Max(depth, 1u) - 1)));
}
