using Godot;

/// <summary>
/// Seconds counted in physics ticks. At the normal tick rate this keeps pace with the wall clock; under a fixed frame rate
/// (simulated-time test runs, Movie Maker recordings) it follows the simulated frames, so timers built on it behave in a
/// test or a recording as they do in play. Like a wall clock, it keeps counting while the tree is paused and ignores
/// <see cref="Engine.TimeScale"/>.
/// </summary>
public static class FrameClock
{
	public static double Seconds => Engine.GetPhysicsFrames() / (double)Engine.PhysicsTicksPerSecond;
}