using Godot;

/// <summary>An on-off effect: receivers are active while the source is on, as a gate held open by a weighted plate.</summary>
[Tool, GlobalClass]
public partial class HoldTriggerMode : TriggerMode
{
	public override void Carry(TriggerOutputComponent output, bool on, in TriggerActivation activation) =>
		output.SetTargetsActive(on, activation);
}