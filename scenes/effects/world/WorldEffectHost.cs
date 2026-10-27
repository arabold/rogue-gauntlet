using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Actor/item-local visual lifetime and visibility. Gameplay remains authoritative.</summary>
[GlobalClass]
public partial class WorldEffectHost : Node3D
{
	[Signal] public delegate void GateChangedEventHandler();
	[Export] public Node3D InstancesRoot { get; set; }
	[Export] public Node3D OriginAnchor { get; set; }
	[Export] public Node3D BodyAnchor { get; set; }
	[Export] public Node3D GroundAnchor { get; set; }
	[Export] public SurfaceAppearanceController SurfaceController { get; set; }
	[Export] public HealthComponent Health { get; set; }
	public bool IsStopped { get; private set; }
	public bool IsSuppressed => IsStopped || SurfaceController?.HasCloak == true || _ownerSuppressed;
	public int ActiveInstanceCount => _instances.Count;
	public int LeaseCount => _sustained.Values.Sum(s => s.Sources.Count);
	public float PlaybackSpeed { get; set; } = 1;
	private sealed class Sustained
	{
		public HashSet<object> Sources = [];
		public WorldEffectInstance Instance;
	}
	private readonly Dictionary<WorldEffectDefinition, Sustained> _sustained = [];
	private readonly List<WorldEffectInstance> _instances = [];
	private bool _ownerSuppressed;

	public override void _Ready()
	{
		VisualHelper.Mark(this);
		if (Health != null) this.SubscribeUntilExit(Health, h => h.Died += StopForDeath, h => h.Died -= StopForDeath);
		if (SurfaceController != null) this.SubscribeUntilExit(SurfaceController, c => c.StateChanged += RefreshGate, c => c.StateChanged -= RefreshGate);
		if (Health?.IsDead == true) StopForDeath();
		// Equipped presenters can enter before this host is ready, so publish the initial cloak gate too.
		RefreshGate();
	}
	public WorldEffectResult PlayBurst(WorldEffectDefinition definition, uint cosmeticSeed) => PlayBurstCore(definition, cosmeticSeed);
	/// <summary>Inject per-playback visual inputs before validation; shared definitions stay immutable.</summary>
	public WorldEffectResult PlayBurstConfigured(WorldEffectDefinition definition, uint cosmeticSeed, Func<WorldEffectInstance, bool> configure)
		=> PlayBurstCore(definition, cosmeticSeed, configure);
	/// <summary>Leave a cue at the operation's world position even if its actor moves immediately.</summary>
	public WorldEffectResult PlayBurstAt(WorldEffectDefinition definition, uint cosmeticSeed, Vector3 position)
		=> PlayBurstCore(definition, cosmeticSeed, worldPosition: position);
	private WorldEffectResult PlayBurstCore(WorldEffectDefinition definition, uint cosmeticSeed, Func<WorldEffectInstance, bool> configure = null, Vector3? worldPosition = null)
	{
		if (!Valid(definition, WorldEffectLifetime.Burst)) return WorldEffectResult.Rejected;
		if (IsSuppressed && definition.SuppressWhileInvisible) return WorldEffectResult.Suppressed;
		var existing = _instances.Where(i => i.Definition == definition && !i.IsEnding).ToArray();
		if (existing.Length > 0 && definition.Retrigger == WorldEffectRetrigger.IgnoreWhileActive) return WorldEffectResult.Unchanged;
		// Preflight before retiring an existing effect, so a malformed replacement never removes it.
		var instance = Prepare(definition, configure);
		if (instance == null) return WorldEffectResult.Rejected;
		if (definition.Retrigger == WorldEffectRetrigger.Restart) foreach (var old in existing) old.Stop(true);
		else if (existing.Length >= definition.MaxConcurrent) existing[0].Stop(true);
		Start(instance, definition, cosmeticSeed);
		if (worldPosition is Vector3 position) { instance.TopLevel = true; instance.GlobalPosition = position; }
		return WorldEffectResult.Started;
	}
	/// <summary>Own a sustained effect by source; immediate requests finish entry or an unowned exit through this host.</summary>
	public WorldEffectResult SetSustained(object source, WorldEffectDefinition definition, bool active, uint cosmeticSeed, Func<WorldEffectInstance, bool> configure = null, bool immediate = false)
	{
		var result = SetSustainedCore(source, definition, active, cosmeticSeed, configure);
		if (!immediate || result == WorldEffectResult.Rejected) return result;
		if (active && _sustained.TryGetValue(definition, out var state)) state.Instance.EnterImmediately();
		else if (!active && !_sustained.ContainsKey(definition))
			foreach (var instance in _instances.Where(i => i.Definition == definition && i.IsEnding).ToArray()) instance.Stop(true);
		return result;
	}
	private WorldEffectResult SetSustainedCore(object source, WorldEffectDefinition definition, bool active, uint cosmeticSeed, Func<WorldEffectInstance, bool> configure)
	{
		if (source == null || definition == null) return WorldEffectResult.Rejected;
		if (!active)
		{
			if (!_sustained.TryGetValue(definition, out var entry) || !entry.Sources.Remove(source)) return WorldEffectResult.Unchanged;
			if (entry.Sources.Count == 0) { _sustained.Remove(definition); entry.Instance?.Stop(false); }
			return WorldEffectResult.Started;
		}
		if (!Valid(definition, WorldEffectLifetime.Sustained)) return WorldEffectResult.Rejected;
		if (_sustained.TryGetValue(definition, out var state))
			return state.Sources.Add(source) ? WorldEffectResult.Started : WorldEffectResult.Unchanged;
		var retiring = configure == null ? _instances.FirstOrDefault(i => i.Definition == definition && i.IsEnding) : null;
		if (retiring != null)
		{
			state = new Sustained { Instance = retiring }; state.Sources.Add(source);
			_sustained.Add(definition, state); retiring.Resume(); return WorldEffectResult.Started;
		}
		var instance = Prepare(definition, configure);
		if (instance == null) return WorldEffectResult.Rejected;
		// A sustained restart can truncate its old tail, but cannot accumulate unbounded retiring instances.
		foreach (var old in _instances.Where(i => i.Definition == definition && i.IsEnding).ToArray()) old.Stop(true);
		state = new Sustained() { Instance = instance }; state.Sources.Add(source);
		_sustained.Add(definition, state); Start(instance, definition, cosmeticSeed);
		return WorldEffectResult.Started;
	}
	private bool Valid(WorldEffectDefinition definition, WorldEffectLifetime lifetime)
	{
		return !IsStopped && IsInsideTree() && InstancesRoot != null && definition?.Scene != null
			&& definition.Lifetime == lifetime && definition.MaxConcurrent is >= 1 and <= 8 && AnchorFor(definition) != null;
	}
	private WorldEffectInstance Prepare(WorldEffectDefinition definition, Func<WorldEffectInstance, bool> configure = null)
	{
		var node = definition.Scene.Instantiate();
		if (node is WorldEffectInstance effect && (configure == null || configure(effect)) && effect.Validate(SurfaceController)) return effect;
		GD.PushWarning($"{Name}: invalid world effect {definition.Id}; check scene, timeline and surface assignments."); node.Free(); return null;
	}
	private Node3D AnchorFor(WorldEffectDefinition definition) => definition.Anchor switch
	{ WorldEffectAnchor.Body => BodyAnchor, WorldEffectAnchor.Ground => GroundAnchor, _ => OriginAnchor };
	private void Start(WorldEffectInstance instance, WorldEffectDefinition definition, uint seed)
	{
		_instances.Add(instance); instance.Definition = definition;
		instance.Finished += () => _instances.Remove(instance);
		InstancesRoot.AddChild(instance);
		instance.Position = InstancesRoot.ToLocal(AnchorFor(definition).GlobalPosition);
		instance.Start(this, seed);
		instance.SetSuppressed(IsSuppressed && definition.SuppressWhileInvisible);
	}
	/// <summary>Propagated explicitly from an equipped item's actor; no global actor lookup.</summary>
	public void SetOwnerSuppressed(bool suppressed)
	{
		if (_ownerSuppressed == suppressed) return;
		_ownerSuppressed = suppressed; RefreshGate();
	}
	private void RefreshGate()
	{
		foreach (var instance in _instances.ToArray()) instance.SetSuppressed(IsSuppressed && instance.Definition.SuppressWhileInvisible);
		EmitSignalGateChanged();
	}
	/// <summary>Finish committed relocation callbacks before saving; temporary presentation needs no saved state.</summary>
	public void CompleteSurfaceTransitions()
	{
		foreach (var instance in _instances.Where(i => i.Surface?.Kind == SurfaceAppearanceKind.Transition).ToArray()) instance.Stop(true);
	}
	public void Clear(bool immediate = true)
	{
		_sustained.Clear(); foreach (var instance in _instances.ToArray()) instance.Stop(immediate);
	}
	/// <summary>Terminal handoff: later cues cannot replace materials used by a corpse fade.</summary>
	public void StopForDeath()
	{
		if (IsStopped) return;
		IsStopped = true; Clear(); SurfaceController?.ClearAndRestore(true);
		EmitSignalGateChanged();
	}
	public override void _ExitTree() => Clear();
}