using Godot;

/// <summary>
/// Step cost on the clearance grid: a step's length, raised where the cell is tight for the body so routes keep to the
/// middle of corridors and doorways and round corners instead of brushing them. Open space costs only its length.
/// </summary>
public readonly record struct NavigationCosts(float CenterBias, float PreferredMargin)
{
	/// <summary>Cost multiplier for a body of <paramref name="radius"/> entering a cell with <paramref name="clearance"/>.</summary>
	public float Multiplier(float clearance, float radius)
	{
		if (CenterBias <= 0 || PreferredMargin <= 0) return 1;
		float tightness = Mathf.Clamp((radius + PreferredMargin - clearance) / PreferredMargin, 0, 1);
		return 1 + CenterBias * tightness;
	}
}