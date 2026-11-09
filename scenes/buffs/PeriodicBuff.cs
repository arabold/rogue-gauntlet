using Godot;

[Tool]
[GlobalClass]
public abstract partial class PeriodicBuff : Buff
{
	/// <summary>
	/// Ticks per second for the buff.
	/// </summary>
	[Export] public float TicksPerSecond = 1;

	/// <summary>
	/// Callback for an actor-neutral periodic effect.
	/// </summary>
	public virtual void OnTick(IBuffTarget target)
	{ }
}
