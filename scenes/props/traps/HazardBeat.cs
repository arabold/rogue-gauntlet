/// <summary>
/// How one hazard of a gauntlet moves: its <see cref="Period"/> in seconds, its <see cref="Phase"/> (0 to 1) within the
/// gauntlet's clock, and for swinging hazards how far the blade travels to each side (<see cref="Sweep"/>, metres; 0
/// keeps the authored sweep). Patterns and room steps compute it; a hazard applies the parts it has.
/// </summary>
public readonly record struct HazardBeat(float Period, float Phase, float Sweep = 0);
