/// <summary>Cross-actor physics ordering; lower values run first.</summary>
public static class CombatPhysics
{
	public const int Input = -60;
	public const int Intent = -50;
	public const int Defense = -40;
	public const int Steering = -30;
	public const int Clearance = -25;
	public const int Movement = -20;
	public const int Damage = 0;
	public const int Commit = 10;
	public const int Trace = 20;
}