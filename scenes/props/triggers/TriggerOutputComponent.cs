using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// A trigger source's output: whether its source (the parent: a plate, a switch, a condition) is on, and the receivers
/// its changes reach through <see cref="Mode"/>. The source reports every change with <see cref="Report"/>; a report
/// of the state it is already in is ignored, so receivers only see changes. Conditions and quests watch
/// <see cref="Switched"/>. A target that is not an <see cref="ITriggerable"/> is flagged in the editor and skipped.
/// </summary>
[Tool, GlobalClass]
public partial class TriggerOutputComponent : Node
{
	/// <summary>Pulse, Hold or Latch: a shared, stateless strategy from <c>scenes/props/triggers/modes/</c>.</summary>
	[Export] public TriggerMode Mode { get; set; }
	/// <summary>The receivers: <see cref="ITriggerable"/> nodes, such as a trap's spikes or a wall launcher.</summary>
	[Export] public Node[] Targets { get; set; } = [];

	[Signal] public delegate void SwitchedEventHandler(bool on, Node3D activator);

	public bool IsOn { get; private set; }

	/// <summary>Set for good by a latching mode once its receivers turned on.</summary>
	private bool _latched;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint() && Mode == null) GD.PushError($"{GetPath()}: assign a trigger mode.");
	}

	/// <summary>The source turned <paramref name="on"/> or off, by <paramref name="activator"/>.</summary>
	public void Report(bool on, Node3D activator)
	{
		if (Engine.IsEditorHint() || on == IsOn) return;
		IsOn = on;
		Mode?.Carry(this, on, new TriggerActivation(activator));
		EmitSignalSwitched(on, activator);
	}

	public void PulseTargets(in TriggerActivation activation)
	{
		foreach (var receiver in Targets.OfType<ITriggerable>()) receiver.Pulse(activation);
	}

	public void SetTargetsActive(bool active, in TriggerActivation activation)
	{
		foreach (var receiver in Targets.OfType<ITriggerable>()) receiver.SetActive(active, activation);
	}

	/// <summary>Latches the output; false when it already was, so its receivers turn on only once.</summary>
	public bool TryLatch()
	{
		if (_latched) return false;
		_latched = true;
		return true;
	}

	public override string[] _GetConfigurationWarnings()
	{
		var warnings = new List<string>();
		if (Mode == null) warnings.Add("Assign a trigger mode: Pulse, Hold or Latch.");
		if (Targets.Any(target => target == null)) warnings.Add("Remove the empty targets.");
		warnings.AddRange(Targets.Where(target => target != null && target is not ITriggerable)
			.Select(target => $"{target.Name} is not a trigger receiver."));
		return warnings.ToArray();
	}
}