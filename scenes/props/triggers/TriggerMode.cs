using Godot;

/// <summary>
/// How a <see cref="TriggerOutputComponent"/> carries its source's changes to its receivers: a one-time pulse, an on-off
/// hold or a permanent latch. Stateless and shared between outputs: the output keeps whatever a mode remembers.
/// </summary>
[Tool, GlobalClass]
public abstract partial class TriggerMode : Resource
{
	/// <summary>Carries the source turning <paramref name="on"/> or off to <paramref name="output"/>'s receivers.</summary>
	public abstract void Carry(TriggerOutputComponent output, bool on, in TriggerActivation activation);
}