using Godot;
using System.Collections.Generic;

/// <summary>Delays and holds the workbench's frozen appearance and pose until the preview expires.</summary>
public partial class FrozenPreview : Node
{
	public WorldEffectHost Host { get; set; }
	public WorldEffectDefinition Effect { get; set; }
	public Node Visual { get; set; }
	public float StartDelay { get; set; }
	private readonly Dictionary<AnimationPlayer, float> _speeds = [];
	private float _time;
	private const float Lifetime = 2.1f, ExitTime = .4f;
	private bool _started, _leased;
	private void Acquire()
	{
		Host.ProcessMode = ProcessModeEnum.Always;
		_leased = Host.SetSustained(this, Effect, true, 0) is WorldEffectResult.Started or WorldEffectResult.Unchanged;
		if (!_leased) { QueueFree(); return; }
		_started = true;
		foreach (var node in Visual.FindChildren("*", "AnimationPlayer", true, false))
			if (node is AnimationPlayer animation) { _speeds[animation] = animation.SpeedScale; animation.SpeedScale = 0; }
	}
	public void Refresh()
	{
		_time = 0; StartDelay = 0;
		if (_started && !_leased) _leased = Host.SetSustained(this, Effect, true, 0) is WorldEffectResult.Started or WorldEffectResult.Unchanged;
	}
	public override void _Process(double delta)
	{
		_time += (float)delta;
		if (_time < StartDelay) return;
		if (!_started) { _time -= StartDelay; StartDelay = 0; Acquire(); if (!_started) return; }
		if (_leased && _time >= Lifetime - ExitTime) { Host.SetSustained(this, Effect, false, 0); _leased = false; }
		if (_time >= Lifetime) QueueFree();
	}
	public override void _ExitTree()
	{
		if (_leased && GodotObject.IsInstanceValid(Host)) Host.SetSustained(this, Effect, false, 0);
		foreach (var pair in _speeds) if (GodotObject.IsInstanceValid(pair.Key)) pair.Key.SpeedScale = pair.Value;
	}
}