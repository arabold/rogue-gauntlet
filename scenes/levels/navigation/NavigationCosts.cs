using Godot;

/// <summary>
/// Step cost on the clearance grid: a step's length, raised where the cell is tight for the body so routes keep to the
/// middle of corridors and doorways and round corners instead of brushing them. Open space costs only its length.
/// </summary>
public readonly record struct NavigationCosts(float CenterBias, float PreferredMargin)
{
	/// <summary>Cost multiplier for a body of <paramref name="radius"/> entering a cell with <paramref name="clearance"/>.</summary>
	public float Multiplier(float clearance, float radius) => CenterBias <= 0 ? 1 : 1 + CenterBias * Tightness(clearance, radius);

	/// <summary>How far short of the body radius plus <see cref="PreferredMargin"/> the clearance falls: 0 (roomy) to 1.</summary>
	public float Tightness(float clearance, float radius) =>
		PreferredMargin <= 0 ? 0 : Mathf.Clamp((radius + PreferredMargin - clearance) / PreferredMargin, 0, 1);

	/// <summary>Cost of a body of <paramref name="radius"/> stepping between neighboring cells.</summary>
	public float StepCost(ClearanceGrid grid, Vector2I from, Vector2I to, float radius) =>
		(from.X != to.X && from.Y != to.Y ? grid.CellSize * Mathf.Sqrt2 : grid.CellSize) * Multiplier(grid.ClearanceAt(to), radius);
}