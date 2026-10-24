using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>Scene-scoped JSONL capture. Records use stable paths plus execution IDs; files are never appended across runs.</summary>
public partial class CombatTraceRecorder : Node
{
	[Export] public bool CaptureFrames { get; set; } = true;
	[Export] public bool EchoToConsole { get; set; }
	public string OutputPath { get; private set; } = "";
	private Node _scope;
	private StreamWriter _writer;
	private ulong _firstFrame;
	private ulong _sequence;
	private double _time;
	private string _run;
	private readonly Queue<string> _recent = new();
	public string RecentRecordsJson => "[" + string.Join(",", _recent) + "]";

	public override void _Ready() => ProcessPhysicsPriority = CombatPhysics.Trace;

	/// <summary>Begin a fresh capture after actors are initialized, before issuing scenario commands.</summary>
	public void Start(Node scope, ulong seed, string path = "")
	{
		Stop();
		_scope = scope;
		_firstFrame = Engine.GetPhysicsFrames();
		_sequence = 0;
		_time = 0;
		_recent.Clear();
		_run = Guid.NewGuid().ToString("N");
		OutputPath = "";
		if (!string.IsNullOrEmpty(path))
		{
			OutputPath = path.StartsWith("user://") || path.StartsWith("res://") ? ProjectSettings.GlobalizePath(path) : Path.GetFullPath(path);
			_writer = new StreamWriter(OutputPath, false) { AutoFlush = true };
		}
		CombatTrace.Recorded += Record;
		Record(scope, "run.started", new Dictionary<string, object>
		{
			["seed"] = seed, ["physics_ticks_per_second"] = Engine.PhysicsTicksPerSecond,
			["scene"] = scope.SceneFilePath, ["engine"] = Engine.GetVersionInfo()["string"].AsString(),
		});
	}

	private void Record(Node source, string eventId, IReadOnlyDictionary<string, object> fields)
	{
		if (_scope == null || source == null || (!ReferenceEquals(source, _scope) && !_scope.IsAncestorOf(source))) return;
		var entry = new Dictionary<string, object>
		{
			["schema"] = 1, ["run"] = _run, ["sequence"] = ++_sequence, ["frame"] = Engine.GetPhysicsFrames() - _firstFrame,
			["time"] = _time, ["event"] = eventId, ["actor"] = CombatTrace.Identity(source), ["data"] = fields,
		};
		string json = JsonSerializer.Serialize(entry);
		_recent.Enqueue(json);
		while (_recent.Count > 2048) _recent.Dequeue();
		_writer?.WriteLine(json);
		if (EchoToConsole) GD.Print("COMBAT " + json);
	}

	public void AdvanceClock(double delta) => _time += delta;

	public override void _PhysicsProcess(double delta)
	{
		if (_scope == null) return;
		if (!CaptureFrames) return;
		CombatTrace.Write(_scope, "simulation.tick", ("delta", delta), ("time_scale", Engine.TimeScale));
		foreach (Node node in _scope.GetChildren())
		{
			if (node is not Node3D actor || actor.GetNodeOrNull<HealthComponent>("HealthComponent") == null) continue;
			var movement = actor.GetNodeOrNull<MovementComponent>("MovementComponent");
			var gate = actor.GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent");
			var attack = actor.GetNodeOrNull<AttackController>("AttackController") ?? actor.GetNodeOrNull<AttackController>("PlayerAttackController");
			var dodge = actor.GetNodeOrNull<DodgeComponent>("DodgeComponent");
			var guard = actor.GetNodeOrNull<GuardComponent>("GuardComponent");
			CombatTrace.Write(actor, "actor.state", ("position", CombatTrace.Position(actor)),
				("velocity", CombatTrace.Vector((actor as CharacterBody3D)?.Velocity ?? Vector3.Zero)), ("facing", CombatTrace.Vector(movement?.LookAtDirection ?? Vector3.Forward)),
				("health", actor.GetNodeOrNull<HealthComponent>("HealthComponent")?.CurrentHealth),
				("dead", actor.GetNodeOrNull<HealthComponent>("HealthComponent")?.IsDead), ("disabled", gate?.Disabled),
				("action", gate?.CurrentKind.ToString()), ("phase", gate?.CurrentPhase.ToString()), ("stagger_remaining", gate?.StaggerRemaining),
				("execution", attack?.CurrentExecutionId.Sequence), ("attacking", attack?.IsAttacking),
				("dodge_remaining", dodge?.RecoveryRemaining), ("dodge_cooldown", dodge?.CooldownRemaining),
				("guard_elapsed", guard?.ElapsedTime), ("parry_window_open", guard?.ParryWindowOpen),
				("dodge_elapsed", dodge?.ElapsedTime), ("dodge_immunity", dodge?.IsImmune),
				("guarding", guard?.IsGuarding), ("guard_recovery", guard?.RecoveryRemaining), ("parry_rearm", guard?.ParryRearmRemaining));
		}
	}

	public void Stop()
	{
		if (_scope == null) return;
		Record(_scope, "run.ended", new Dictionary<string, object>());
		CombatTrace.Recorded -= Record;
		_writer?.Dispose();
		_writer = null;
		_scope = null;
	}
	public override void _ExitTree() => Stop();
}