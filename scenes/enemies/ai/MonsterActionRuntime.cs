using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Which actions a request may choose from.</summary>
public enum MonsterActionFilter
{
	/// <summary>Every action, offensive and support.</summary>
	All,
	/// <summary>Only actions aimed at the actor or its allies; tactics that forbid attacking still allow these.</summary>
	SupportOnly,
	/// <summary>Only weapon attacks; harnesses and tests use it to trigger a monster's attack.</summary>
	Attacks,
	/// <summary>Only riposte-eligible attacks, after a successful guard.</summary>
	Riposte
}

/// <summary>
/// Per-actor action state: cooldowns, the decision retry delay, repeat history, condition memory and a per-physics-frame
/// cache of which action can start against which target. Strategies, tactics and the behavior host share the cache, so
/// each action is evaluated at most once per frame.
/// </summary>
/// <remarks>
/// Selection keeps the highest priority among actions that are ready (off cooldown, slot available, in position,
/// conditions met, useful) and draws one by weight × modifiers × repeat penalty from the actor's seeded stream.
/// Cooldown and history start only when the chosen action actually starts. The combat profile's action list is
/// snapshotted when first used, so the frame loop never walks Godot collections.
/// </remarks>
public sealed class MonsterActionRuntime(EnemyContext context)
{
	private readonly record struct Entry(MonsterAction Action, MonsterActionCondition[] Conditions, MonsterActionWeightModifier[] Modifiers);

	private const float RetryDelay = 0.2f;
	private readonly Dictionary<MonsterAction, Cooldown> _cooldowns = [];
	private readonly List<MonsterAction> _cooldownKeys = [];
	private readonly Dictionary<MonsterActionCondition, object> _conditionState = [];
	private readonly Dictionary<MonsterAction, Node3D> _evaluated = [];
	private readonly List<Node3D> _candidates = [];
	private readonly List<(MonsterAction Action, Node3D Target)> _choices = [];
	private readonly List<float> _weights = [];
	private readonly RandomNumberGenerator _random = new();
	private EnemyCombatProfile _snapshotProfile;
	private Entry[] _entries = [];
	private Dictionary<MonsterAction, int> _entryIndex = [];
	private MonsterActionCondition[] _tickedConditions = [];
	private MonsterAction _primary;
	private ulong _evaluatedFrame = ulong.MaxValue;
	private Cooldown _retry;
	private MonsterAction _lastAccepted;
	private (MonsterAction Action, Node3D Target, ActorActionRequestResult Result) _lastDecision;

	public bool DecisionReady => _retry.IsReady;
	/// <summary>The current combat profile's actions in authored order.</summary>
	public IEnumerable<MonsterAction> Actions => Entries.Select(entry => entry.Action);

	/// <summary>The last selection and its outcome, for traces and the tuning panel; formatted only when read.</summary>
	public string LastDecision
	{
		get
		{
			var (action, target, result) = _lastDecision;
			if (action == null) return "none";
			if (result != ActorActionRequestResult.Success) return $"{action.Label} rejected: {result}";
			return $"{action.Label} → {(ReferenceEquals(target, context.Actor) ? "self" : target?.Name.ToString() ?? "none")}";
		}
	}

	public void ConfigureRandom(ulong seed) => _random.Seed = seed;

	private Entry[] Entries
	{
		get
		{
			EnsureSnapshot();
			return _entries;
		}
	}

	/// <summary>Snapshot the combat profile's actions once per profile with everything the frame loop needs.</summary>
	private void EnsureSnapshot()
	{
		var profile = context.CombatProfile;
		if (ReferenceEquals(profile, _snapshotProfile)) return;
		_snapshotProfile = profile;
		_entries = profile?.Actions?.Where(action => action != null).Select(action => new Entry(action,
			action.Conditions.Where(condition => condition != null).ToArray(),
			action.WeightModifiers.Where(modifier => modifier?.Condition != null).ToArray())).ToArray() ?? [];
		_entryIndex = _entries.Select((entry, index) => (entry.Action, index)).ToDictionary(pair => pair.Action, pair => pair.index);
		_tickedConditions = _entries.SelectMany(entry => entry.Conditions.Concat(entry.Modifiers.Select(modifier => modifier.Condition)))
			.Distinct().ToArray();
		_primary = _entries.Select(entry => entry.Action).FirstOrDefault(action => action.CanAnchorEngagement);
	}

	/// <summary>The combat profile's first offensive attack or spell: movement strategies use its range and slot.</summary>
	public MonsterAction PrimaryOffense
	{
		get
		{
			EnsureSnapshot();
			return _primary;
		}
	}

	/// <summary>Advance cooldowns, the retry delay and condition memory. Runs every physics frame, even while acting.</summary>
	public void Tick(double delta)
	{
		_retry.Tick(delta);
		if (_cooldowns.Count > 0)
		{
			_cooldownKeys.Clear();
			_cooldownKeys.AddRange(_cooldowns.Keys);
			foreach (var action in _cooldownKeys)
			{
				var cooldown = _cooldowns[action];
				if (cooldown.Tick(delta)) _cooldowns.Remove(action);
				else _cooldowns[action] = cooldown;
			}
		}
		EnsureSnapshot();
		foreach (var condition in _tickedConditions) condition.Tick(context, delta);
	}

	/// <summary>
	/// This actor's memory for a stateful condition (a timer, a counter), created on first use. Shared condition Resources
	/// stay stateless; the memory resets when the target changes.
	/// </summary>
	public T State<T>(MonsterActionCondition condition) where T : class, new()
	{
		if (_conditionState.TryGetValue(condition, out var state)) return (T)state;
		var created = new T();
		_conditionState[condition] = created;
		return created;
	}

	public bool IsCoolingDown(MonsterAction action) => _cooldowns.ContainsKey(action);
	public float CooldownRemaining(MonsterAction action) => _cooldowns.TryGetValue(action, out var cooldown) ? cooldown.Remaining : 0;

	/// <summary>
	/// The first candidate the action can start against this frame (position, conditions, usefulness), ignoring cooldown
	/// and slots; null when there is none. Cached per physics frame.
	/// </summary>
	public Node3D EvaluateTarget(MonsterAction action)
	{
		ulong frame = Engine.GetPhysicsFrames();
		if (frame != _evaluatedFrame)
		{
			_evaluatedFrame = frame;
			_evaluated.Clear();
		}
		if (_evaluated.TryGetValue(action, out var cached)) return cached;
		Node3D found = null;
		if (TryGetEntry(action, out var entry) && action.IsAvailable(context))
		{
			var selector = action.EffectiveTarget;
			_candidates.Clear();
			selector.CollectCandidates(context, action, _candidates);
			foreach (var candidate in _candidates)
				if (action.IsInPosition(context, candidate) && ConditionsMet(entry, candidate)
					&& selector.CanSee(context, candidate) && action.IsUsefulOn(context, candidate))
				{
					found = candidate;
					break;
				}
		}
		_evaluated[action] = found;
		return found;
	}

	private bool TryGetEntry(MonsterAction action, out Entry entry)
	{
		var entries = Entries;
		bool found = _entryIndex.TryGetValue(action, out int index);
		entry = found ? entries[index] : default;
		return found;
	}

	private bool ConditionsMet(Entry entry, Node3D target)
	{
		foreach (var condition in entry.Conditions)
			if (!condition.IsMet(context, target)) return false;
		return true;
	}

	/// <summary>The action's weight for this target after its condition-driven modifiers.</summary>
	public float ResolveWeight(MonsterAction action, Node3D target)
	{
		float weight = action.Weight;
		if (!TryGetEntry(action, out var entry)) return weight;
		foreach (var modifier in entry.Modifiers)
			if (modifier.Condition.IsMet(context, target)) weight *= modifier.Multiplier;
		return weight;
	}

	private bool CanTakeSlot(MonsterAction action) => action.ReservedSlot is not { } kind || context.CanTakeSlot(kind);

	/// <summary>The primary offensive action can start now as far as position, conditions and slots go (cooldown aside).</summary>
	public bool CanEngage => PrimaryOffense is { } primary && EvaluateTarget(primary) != null && CanTakeSlot(primary);

	/// <summary>The target lies within the primary action's range and sight, even if it cannot start yet (facing, slot).</summary>
	public bool IsPrimaryInReach => PrimaryOffense is { } primary && GodotObject.IsInstanceValid(context.Target)
		&& context.HasLineOfSightToTarget() && primary.IsInRange(context.HorizontalTargetDistance);

	/// <summary>
	/// Whether the actor can attack right now: the primary can engage, or some other offensive action is ready (off
	/// cooldown, slot free, in position). Tactical fallbacks use it to decide between holding to attack and approaching.
	/// </summary>
	public bool CanActNow
	{
		get
		{
			if (CanEngage) return true;
			foreach (var entry in Entries)
				if (entry.Action.IsOffensive && !IsCoolingDown(entry.Action) && CanTakeSlot(entry.Action) && EvaluateTarget(entry.Action) != null)
					return true;
			return false;
		}
	}

	/// <summary>Whether the primary action's slot is free; actions without a slot always are.</summary>
	public bool CanTakePrimarySlot => PrimaryOffense is not { } primary || CanTakeSlot(primary);

	/// <summary>Choose one ready action and its target, or null. Never changes cooldowns or history.</summary>
	public (MonsterAction Action, Node3D Target)? Select(MonsterActionFilter filter)
	{
		if (!DecisionReady) return null;
		_choices.Clear();
		_weights.Clear();
		int priority = int.MinValue;
		float repeat = context.CombatProfile?.RepeatWeightMultiplier ?? 1;
		foreach (var entry in Entries)
		{
			var action = entry.Action;
			if (!Admits(filter, action) || IsCoolingDown(action) || !CanTakeSlot(action)) continue;
			var target = EvaluateTarget(action);
			if (target == null) continue;
			float weight = ResolveWeight(action, target) * (ReferenceEquals(action, _lastAccepted) ? repeat : 1);
			if (!(weight > 0) || action.Priority < priority) continue;
			if (action.Priority > priority)
			{
				priority = action.Priority;
				_choices.Clear();
				_weights.Clear();
			}
			_choices.Add((action, target));
			_weights.Add(weight);
		}
		if (_choices.Count == 0) return null;
		int index = Weighted.PickIndex(_weights, _random);
		return index < 0 ? null : _choices[index];
	}

	private static bool Admits(MonsterActionFilter filter, MonsterAction action) => filter switch
	{
		MonsterActionFilter.SupportOnly => !action.IsOffensive,
		MonsterActionFilter.Attacks => action is MonsterAttackAction,
		MonsterActionFilter.Riposte => action.CanRiposte,
		_ => true,
	};

	/// <summary>Start the cooldown and repeat history of an action that actually started.</summary>
	public void Accepted(MonsterAction action, Node3D target)
	{
		_lastAccepted = action;
		if (action.Cooldown > 0)
		{
			var cooldown = new Cooldown();
			cooldown.Start(action.Cooldown);
			_cooldowns[action] = cooldown;
		}
		_retry.Start(RetryDelay);
		_lastDecision = (action, target, ActorActionRequestResult.Success);
	}

	/// <summary>Wait a short while before the next attempt instead of retrying every frame.</summary>
	public void Rejected(MonsterAction action, ActorActionRequestResult result)
	{
		_retry.Start(RetryDelay);
		_lastDecision = (action, null, result);
	}

	/// <summary>Forget condition memory and this frame's evaluations, which depend on the target, when it changes.</summary>
	public void ClearTargetMemory()
	{
		_conditionState.Clear();
		_evaluatedFrame = ulong.MaxValue;
	}
}
