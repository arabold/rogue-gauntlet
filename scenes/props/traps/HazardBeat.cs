/// <summary>
/// How one hazard of a gauntlet moves: its <see cref="Period"/> in seconds and its <see cref="Phase"/> (0 to 1) within
/// the gauntlet's clock. For a swinging hazard, <see cref="Space"/> is the free distance from its middle to whatever
/// stands beside it at its closest (a wall, or halfway to a neighbouring blade), in metres; 0 leaves it unconstrained.
/// Patterns and room steps compute it; each hazard applies the parts it has.
/// </summary>
public readonly record struct HazardBeat(float Period, float Phase, float Space = 0);
