using Godot;
using System.Collections.Generic;

/// <summary>Actor-local action cooldown, pressure and weighted selection state.</summary>
public sealed class EnemyActionSelector
{
	private const float RetryDelay = 0.2f;
	private readonly Dictionary<ulong, Cooldown> _cooldowns = [];
	private readonly Dictionary<ulong, float> _pressure = [];
	private readonly List<EnemyActionChoiceProfile> _candidates = [];
	private readonly List<float> _weights = [];
	private readonly List<ulong> _cooldownKeys = [];
	private Cooldown _retry;
	private readonly RandomNumberGenerator _random = new();
	private EnemyActionProfile _lastAccepted;
	private ulong _pressureTarget;

	public bool DecisionReady => _retry.IsReady;
	public void ConfigureRandom(ulong seed) => _random.Seed = seed;
	public float PressureDurationFor(EnemyActionProfile action) => PressureFor(action);

	/// <summary>Advance per-action timers and maintain close-pressure time without consulting scene groups.</summary>
	public void Tick(double delta, EnemyContext context, EnemyCombatProfile profile)
	{
		_retry.Tick(delta);
		_cooldownKeys.Clear();
		foreach (ulong id in _cooldowns.Keys) _cooldownKeys.Add(id);
		foreach (ulong id in _cooldownKeys)
		{
			var cooldown = _cooldowns[id];
			if (cooldown.Tick(delta)) _cooldowns.Remove(id);
			else _cooldowns[id] = cooldown;
		}
		UpdatePressure((float)delta, context, profile);
	}

	/// <summary>Select one eligible action from the highest priority tier using one deterministic weighted draw.</summary>
	public EnemyActionProfile Select(EnemyCombatProfile profile, EnemyContext context, bool riposteOnly = false, bool attackOnly = false)
	{
		if (!DecisionReady || profile?.Actions == null || context == null) return null;
		_candidates.Clear();
		_weights.Clear();
		int priority = int.MinValue;
		foreach (var choice in profile.Actions)
		{
			var action = choice?.Action;
			if (action == null || !float.IsFinite(choice.Weight) || choice.Weight <= 0
				|| (attackOnly && action is not EnemyAttackProfile)
				|| (riposteOnly && (action is not EnemyAttackProfile || !action.RiposteEligible))
				|| _cooldowns.ContainsKey(action.GetInstanceId())
				|| !action.IsEligible(context, PressureFor(action))
				|| !context.CanTakeActionSlot(action.SlotKind)) continue;
			float weight = choice.Weight * (ReferenceEquals(action, _lastAccepted) ? profile.RepeatWeightMultiplier : 1);
			if (weight <= 0) continue;
			if (action.Priority > priority)
			{
				priority = action.Priority;
				_candidates.Clear();
				_weights.Clear();
			}
			if (action.Priority == priority)
			{
				_candidates.Add(choice);
				_weights.Add(weight);
			}
		}
		return Weighted.Pick(_candidates, _weights, _random)?.Action;
	}

	/// <summary>Start cooldown and repeat history only after the selected action has acquired its execution.</summary>
	public void Accepted(EnemyActionProfile action)
	{
		if (action == null) return;
		_lastAccepted = action;
		if (action.Cooldown > 0)
		{
			var cooldown = new Cooldown();
			cooldown.Start(action.Cooldown);
			_cooldowns[action.GetInstanceId()] = cooldown;
		}
		_retry.Start(RetryDelay);
	}

	public void Rejected() => _retry.Start(RetryDelay);

	private float PressureFor(EnemyActionProfile action) => action == null ? 0 : _pressure.GetValueOrDefault(action.GetInstanceId());

	private void UpdatePressure(float delta, EnemyContext context, EnemyCombatProfile profile)
	{
		ulong targetId = GodotObject.IsInstanceValid(context?.Target) ? context.Target.GetInstanceId() : 0;
		bool visible = targetId != 0 && context.HasLineOfSightToTarget();
		if (!visible || _pressureTarget != targetId) _pressure.Clear();
		_pressureTarget = visible ? targetId : 0;
		if (!visible || profile?.Actions == null) return;
		foreach (var choice in profile.Actions)
		{
			if (choice?.Action is not { ClosePressureDuration: > 0 } action
				|| context.HorizontalTargetDistance > action.ClosePressureRange) continue;
			ulong id = action.GetInstanceId();
			_pressure[id] = Mathf.Min(action.ClosePressureDuration, PressureFor(action) + delta);
		}
	}
}
