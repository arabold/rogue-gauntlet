/// <summary>
/// How a released projectile flies beyond its speed and range: horizontal homing on a captured target after a
/// straight launch, and a launch at a fraction of full speed that ramps up. Straight flight at full speed is the default.
/// </summary>
public readonly record struct ProjectileFlight(float TurnRate, float HomingDelay, float LaunchSpeedScale = 1, float RampDuration = 0)
{
	public static readonly ProjectileFlight Straight = new(0, 0);

	public bool Homes => TurnRate > 0;
}