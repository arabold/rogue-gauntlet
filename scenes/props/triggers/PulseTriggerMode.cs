using Godot;

/// <summary>A one-time effect each time the source turns on: one shot per step onto a plate. Turning off does nothing.</summary>
[Tool, GlobalClass]
public partial class PulseTriggerMode : TriggerMode
{
	public override void Carry(TriggerOutputComponent output, bool on, in TriggerActivation activation)
	{
		if (on) output.PulseTargets(activation);
	}
}