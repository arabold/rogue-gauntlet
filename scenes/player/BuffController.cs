using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Owns runtime buff instances and condition-only status activations for an actor. A buff may impose
/// a <see cref="StatusCondition"/>; the
/// condition is active while at least one active buff carries it, so overlapping sources (poisoned food
/// followed by a poison potion) read as one continuous condition. Observers re-read what they need on
/// <see cref="BuffsChanged"/>.
/// </summary>
[GlobalClass]
public partial class BuffController : Node
{
	/// <summary>Fired once after buffs were applied, removed, expired or cleared.</summary>
	[Signal] public delegate void BuffsChangedEventHandler();

	[Export] public Node3D Actor { get; set; }
	[Export] public HealthComponent Health { get; set; }
	/// <summary>The actor buffs act on; a controller without an <see cref="IBuffTarget"/> actor still serves its own health.</summary>
	public IBuffTarget Target => Actor as IBuffTarget ?? (_standalone ??= new StandaloneTarget(this));
	private StandaloneTarget _standalone;

	private bool ShouldNarratePlayer(bool narrate = true) => Actor is Player && narrate && Health?.IsDead != true;

	/// <summary>Health and buffs only, for controllers wired without a full actor (tests, props).</summary>
	private sealed class StandaloneTarget(BuffController controller) : IBuffTarget
	{
		public HealthComponent Health => controller.Health;
		public BuffController Buffs => controller;
		public IStatModifierTarget Modifiers => controller.Actor as IStatModifierTarget;
	}

	public Array<ActiveBuff> ActiveBuffs { get; } = new();

	public override void _Ready()
	{
		if (Health != null) this.SubscribeUntilExit(Health, h => h.Died += OnDeath, h => h.Died -= OnDeath);
		UpdateProcessing();
	}

	// A periodic buff may be inside its own tick when it kills its owner.
	private void OnDeath() => Callable.From(ClearBuffs).CallDeferred();

	public override void _PhysicsProcess(double delta)
	{
		if (RemoveWhere(active => active.IsExpired, "expired"))
		{
			EmitSignalBuffsChanged();
		}
	}

	/// <summary>Whether any active buff currently imposes <paramref name="condition"/>.</summary>
	public bool HasCondition(StatusCondition condition)
	{
		return condition != null && ActiveBuffs.Any(active => active.Buff.Condition == condition);
	}
	/// <summary>Whether an equivalent shared definition is already active on this actor.</summary>
	public bool HasEquivalent(Buff definition)
	{
		if (definition == null) return false;
		string path = definition.ResourcePath;
		var condition = definition.Condition;
		for (int index = 0; index < ActiveBuffs.Count; index++)
		{
			var active = ActiveBuffs[index].Buff;
			if (ReferenceEquals(active, definition) || (condition != null && active.Condition == condition)
				|| (!string.IsNullOrEmpty(path) && active.ResourcePath == path)) return true;
		}
		return false;
	}
	/// <summary>Whether a live condition activation is still owned by a source.</summary>
	public bool HasStatusFrom(object source) => source != null && ActiveBuffs.Any(a => !a.IsExpired && ReferenceEquals(a.Source, source));

	/// <summary>
	/// Applies <paramref name="buff"/>, first ending the conditions it cures (e.g. healing ends blindness). A refreshing
	/// buff restarts its existing activation instead (see <see cref="Buff.RefreshKey"/>).
	/// </summary>
	public BuffApplication ApplyBuff(Buff buff)
	{
		if (buff?.IsValid != true || Target == null || !buff.CanApplyTo(Target)) return BuffApplication.Rejected;
		bool hadCondition = buff.Condition != null && HasCondition(buff.Condition);
		string key = buff.RefreshKey;
		if (key.Length > 0)
		{
			var existing = ActiveBuffs.FirstOrDefault(active => !active.IsExpired && string.Equals(active.StackKey, key, StringComparison.Ordinal));
			if (existing != null)
			{
				existing.Refresh(buff.Duration);
				Trace("buff.refreshed", existing);
				if (ShouldNarratePlayer())
				{
					SignalBus.EmitPlayerBuffRefreshed(buff);
				}
				EmitSignalBuffsChanged();
				return BuffApplication.Refreshed;
			}
		}

		GameDebug.Combat($"{Actor?.Name ?? Name} applied buff {buff.Name}");
		RemoveWhere(active => active.Buff.Condition != null && buff.CuredConditions.Contains(active.Buff.Condition), "cured");
		AddActivation(buff);
		if (ShouldNarratePlayer())
		{
			SignalBus.EmitPlayerBuffApplied(buff, hadCondition);
		}
		EmitSignalBuffsChanged();
		return BuffApplication.Applied;
	}

	/// <summary>Restore a prevalidated activation without reapplying its one-time cure.</summary>
	public void RestoreBuff(Buff buff, object source, double remaining, double tickProgress)
	{
		AddActivation(buff, source, remaining, tickProgress);
		if (source is IStatusRestoreSource tracked) tracked.StatusRestored(this);
		EmitSignalBuffsChanged();
	}

	/// <summary>Apply or refresh a timed condition owned by a source; other sources remain independent.</summary>
	public bool ApplyStatus(StatusCondition condition, object source, float duration)
	{
		if (condition == null || source == null || !float.IsFinite(duration) || duration <= 0 || Health?.IsDead == true) return false;
		bool hadCondition = HasCondition(condition);
		var existing = ActiveBuffs.FirstOrDefault(a => !a.IsExpired && ReferenceEquals(a.Source, source) && a.Buff.Condition == condition);
		if (existing != null)
		{
			existing.Refresh(duration);
			UpdateProcessing();
		}
		else
		{
			var statusBuff = new Buff { Name = condition.ResourceName, Condition = condition, Duration = duration };
			AddActivation(statusBuff, source);
			if (ShouldNarratePlayer())
			{
				SignalBus.EmitPlayerBuffApplied(statusBuff, hadCondition);
			}
		}
		EmitSignalBuffsChanged();
		return true;
	}

	/// <summary>Enable or disable one persistent condition source without disturbing other sources.</summary>
	public bool SetPersistentStatus(StatusCondition condition, object source, bool enabled, bool narrate = true)
	{
		if (condition == null || source == null || (enabled && Health?.IsDead == true)) return false;
		if (!enabled)
		{
			if (RemoveWhere(a => ReferenceEquals(a.Source, source) && a.Buff.Condition == condition, "removed", narrate)) EmitSignalBuffsChanged();
			return true;
		}
		var existing = ActiveBuffs.FirstOrDefault(a => ReferenceEquals(a.Source, source) && a.Buff.Condition == condition && !a.IsExpired);
		if (existing != null)
		{
			if (!existing.IsPermanent) { existing.Refresh(0); UpdateProcessing(); EmitSignalBuffsChanged(); }
			return true;
		}
		bool hadCondition = HasCondition(condition);
		var statusBuff = new Buff { Name = condition.ResourceName, Condition = condition, Duration = 0 };
		AddActivation(statusBuff, source);
		if (ShouldNarratePlayer(narrate))
		{
			SignalBus.EmitPlayerBuffApplied(statusBuff, hadCondition);
		}
		EmitSignalBuffsChanged();
		return true;
	}

	/// <summary>Apply designer-authored initial conditions as independent persistent sources.</summary>
	public void ApplyInitialConditions(Array<StatusCondition> conditions, object source)
	{
		if (conditions == null || source == null) return;
		foreach (var condition in conditions)
		{
			if (condition == null) { GD.PushWarning($"{Name}: initial conditions contain an empty entry."); continue; }
			SetPersistentStatus(condition, source, true, narrate: false);
		}
	}

	/// <summary>Remove all conditions contributed by one source.</summary>
	public void RemoveStatuses(object source)
	{
		if (source != null && RemoveWhere(active => ReferenceEquals(active.Source, source), "removed")) EmitSignalBuffsChanged();
	}

	public void RemoveBuff(Buff buff)
	{
		var activeBuff = ActiveBuffs.FirstOrDefault(b => b.Buff == buff);
		if (activeBuff != null)
		{
			GD.Print($"{Actor?.Name ?? Name} removed buff {buff.Name}");
			RemoveWhere(active => active == activeBuff, "removed");
			EmitSignalBuffsChanged();
		}
	}

	/// <summary>Removes every active buff, reversing each exactly once, e.g. before a saved player state is restored.</summary>
	public void ClearBuffs()
	{
		if (RemoveWhere(_ => true, "cleared", narrate: false))
		{
			EmitSignalBuffsChanged();
		}
	}

	private void AddActivation(Buff buff, object source = null, double? remaining = null, double tickProgress = 0)
	{
		var active = new ActiveBuff();
		active.Initialize(Target, buff);
		active.SetSource(source);
		if (remaining.HasValue) active.RestoreClock(remaining.Value, tickProgress);
		ActiveBuffs.Add(active);
		AddChild(active);
		UpdateProcessing();
		Trace(remaining.HasValue ? "buff.restored" : "buff.applied", active);
	}

	/// <summary>Logs a buff's start, refresh or end for playtest reports: its name, condition and remaining time.</summary>
	private void Trace(string eventId, ActiveBuff active, string cause = null)
	{
		if (!CombatTrace.Enabled) return;
		CombatTrace.Write(this, eventId, ("buff", active.Buff.Name), ("condition", active.Buff.Condition?.ResourceName),
			("remaining", active.IsPermanent ? 0 : active.RemainingDuration), ("cause", cause));
	}

	private void UpdateProcessing() => SetPhysicsProcess(ActiveBuffs.Any(active => !active.IsPermanent));

	/// <summary>
	/// Reverses and frees every active buff matching <paramref name="predicate"/>, logging the <paramref name="cause"/>.
	/// Callers emit <see cref="BuffsChanged"/> once afterwards, so observers see one change per operation.
	/// </summary>
	private bool RemoveWhere(Func<ActiveBuff, bool> predicate, string cause, bool narrate = true)
	{
		bool shouldNarrate = ShouldNarratePlayer(narrate);
		List<Buff> removedBuffs = null;
		bool removed = false;
		for (int i = ActiveBuffs.Count - 1; i >= 0; i--)
		{
			ActiveBuff activeBuff = ActiveBuffs[i];
			if (!predicate(activeBuff))
			{
				continue;
			}

			if (shouldNarrate)
			{
				(removedBuffs ??= []).Add(activeBuff.Buff);
			}
			activeBuff.Deactivate();
			Trace("buff.ended", activeBuff, cause);
			ActiveBuffs.RemoveAt(i);
			RemoveChild(activeBuff);
			activeBuff.QueueFree();
			removed = true;
		}

		if (removed) UpdateProcessing();
		if (removedBuffs != null)
		{
			EmitEndedBuffs(removedBuffs);
		}
		return removed;
	}

	private void EmitEndedBuffs(List<Buff> removedBuffs)
	{
		HashSet<StatusCondition> endedConditions = null;
		foreach (Buff buff in removedBuffs)
		{
			if (buff == null)
			{
				continue;
			}

			if (buff.Condition is { } condition)
			{
				if (HasCondition(condition) || !(endedConditions ??= []).Add(condition))
				{
					continue;
				}
				SignalBus.EmitPlayerBuffEnded(buff);
				continue;
			}

			if (buff is TimeScaleBuff { TimeScale: < 1f } && ActiveBuffs.Any(a => a.Buff is TimeScaleBuff { TimeScale: < 1f }))
			{
				continue;
			}

			SignalBus.EmitPlayerBuffEnded(buff);
		}
	}
}
