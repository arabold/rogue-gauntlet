using Godot;

/// <summary>A permanent effect: the first time the source turns on, receivers become active for good.</summary>
[Tool, GlobalClass]
public partial class LatchTriggerMode : TriggerMode
{
	public override void Carry(TriggerOutputComponent output, bool on, in TriggerActivation activation)
	{
		if (on && output.TryLatch()) output.SetTargetsActive(true, activation);
	}
}