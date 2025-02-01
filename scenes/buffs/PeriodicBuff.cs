using Godot;

[GlobalClass]
public abstract partial class PeriodicBuff : Buff
{
	/// <summary>
	/// Ticks per second for the buff.
	/// </summary>
	[Export] public float TicksPerSecond = 1;

	/// <summary>
	/// Callback to override for custom behavior when 
	/// the buff is applied to a player.
	/// </summary>
	public virtual void OnTick(Player player)
	{ }
}
