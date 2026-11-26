using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A trigger source's output: whether its source (the parent: a plate, a switch, a condition) is on, and the receivers
/// its changes reach through <see cref="Mode"/>. The source reports every change with <see cref="Report"/>; a report
/// of the state it is already in is ignored, so receivers only see changes. Conditions and quests watch
/// <see cref="Switched"/>. A target that is not an <see cref="ITriggerable"/> is reported, never a crash.
/// </summary>
[Tool, GlobalClass]
public partial class TriggerOutputComponent : Node
{
	/// <summary>Pulse, Hold or Latch: a shared, stateless strategy from <c>scenes/props/triggers/modes/</c>.</summary>
	[Export] public TriggerMode Mode { get; set; }
	[Export] public Node[] Targets { get; set; } = [];

	[Signal] public delegate void SwitchedEventHandler(bool on, Node3D activator);

	public bool IsOn { get; private set; }
	/// <summary>Set for good by a latching mode once its receivers turned on.</summary>
	public bool IsLatched { get; private set; }

	private ITriggerable[] _receivers = [];

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (Mode == null) GD.PushError($"{GetPath()}: assign a trigger mode.");
		_receivers = Targets.OfType<ITriggerable>().ToArray();
		foreach (var target in Targets.Where(target => target is not ITriggerable))
			GD.PushWarning($"{GetPath()}: {target?.Name ?? "an empty target"} is not a trigger receiver.");
	}

	/// <summary>The source turned <paramref name="on"/> or off, by <paramref name="activator"/>.</summary>
	public void Report(bool on, Node3D activator)
	{
		if (Engine.IsEditorHint() || on == IsOn) return;
		IsOn = on;
		Mode?.Carry(this, on, new TriggerActivation(GetParent(), activator));
		EmitSignalSwitched(on, activator);
	}

	public void PulseTargets(in TriggerActivation activation)
	{
		foreach (var receiver in _receivers) receiver.Pulse(activation);
	}

	public void SetTargetsActive(bool active, in TriggerActivation activation)
	{
		foreach (var receiver in _receivers) receiver.SetActive(active, activation);
	}

	/// <summary>Latches the output; false when it already was, so its receivers turn on only once.</summary>
	public bool TryLatch()
	{
		if (IsLatched) return false;
		IsLatched = true;
		return true;
	}

	public override string[] _GetConfigurationWarnings()
	{
		var warnings = new List<string>();
		if (Mode == null) warnings.Add("Assign a trigger mode: Pulse, Hold or Latch.");
		if (Targets.Any(target => target == null)) warnings.Add("Remove the empty targets.");
		return warnings.ToArray();
	}
}