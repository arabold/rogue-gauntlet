/// <summary>
/// How a released projectile flies beyond its speed and range: horizontal homing on a captured target after a
/// straight launch, a launch at a fraction of full speed that ramps up, and a lob that arcs <see cref="ArcHeight"/>
/// metres over cover to where its target stood. Straight flight at full speed is the default.
/// </summary>
public readonly record struct ProjectileFlight(float TurnRate, float HomingDelay, float LaunchSpeedScale = 1, float RampDuration = 0,
	float ArcHeight = 0)
{
	public static readonly ProjectileFlight Straight = new(0, 0);

	public bool Homes => TurnRate > 0;
	public bool Lobs => ArcHeight > 0;
}
