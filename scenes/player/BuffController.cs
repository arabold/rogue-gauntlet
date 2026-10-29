using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// Owns runtime buff instances and condition-only status activations for an actor. Existing buff callbacks
/// remain player-specific. A buff may impose a <see cref="StatusCondition"/>; the
/// condition is active while at least one active buff carries it, so overlapping sources (poisoned food
/// followed by a poison potion) read as one continuous condition. Observers re-read what they need on
/// <see cref="BuffsChanged"/>.
/// </summary>
public partial class BuffController : Node
{
	/// <summary>Fired once after buffs were applied, removed, expired or cleared.</summary>
	[Signal] public delegate void BuffsChangedEventHandler();

	[Export] public Player Player { get; set; }
	[Export] public HealthComponent Health { get; set; }

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
		if (RemoveWhere(active => active.IsExpired))
		{
			EmitSignalBuffsChanged();
		}
	}

	/// <summary>Whether any active buff currently imposes <paramref name="condition"/>.</summary>
	public bool HasCondition(StatusCondition condition)
	{
		return condition != null && ActiveBuffs.Any(active => active.Buff.Condition == condition);
	}
	/// <summary>Whether a live condition activation is still owned by a source.</summary>
	public bool HasStatusFrom(object source) => source != null && ActiveBuffs.Any(a => !a.IsExpired && ReferenceEquals(a.Source, source));

	/// <summary>Applies <paramref name="buff"/>, first ending the conditions it cures (e.g. healing ends blindness).</summary>
	public void ApplyBuff(Buff buff)
	{
		GD.Print($"{Player?.Name ?? Name} applied buff {buff.Name}");
		RemoveWhere(active => active.Buff.Condition != null && buff.CuredConditions.Contains(active.Buff.Condition));
		AddActivation(buff);
		EmitSignalBuffsChanged();
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
		var existing = ActiveBuffs.FirstOrDefault(a => !a.IsExpired && ReferenceEquals(a.Source, source) && a.Buff.Condition == condition);
		if (existing != null) { existing.RefreshStatus(duration); UpdateProcessing(); }
		else
			AddActivation(new Buff { Name = condition.ResourceName, Condition = condition, Duration = duration }, source);
		EmitSignalBuffsChanged();
		return true;
	}

	/// <summary>Enable or disable one persistent condition source without disturbing other sources.</summary>
	public bool SetPersistentStatus(StatusCondition condition, object source, bool enabled)
	{
		if (condition == null || source == null || (enabled && Health?.IsDead == true)) return false;
		if (!enabled)
		{
			if (RemoveWhere(a => ReferenceEquals(a.Source, source) && a.Buff.Condition == condition)) EmitSignalBuffsChanged();
			return true;
		}
		var existing = ActiveBuffs.FirstOrDefault(a => ReferenceEquals(a.Source, source) && a.Buff.Condition == condition && !a.IsExpired);
		if (existing != null)
		{
			if (!existing.IsPermanent) { existing.RefreshStatus(0); UpdateProcessing(); EmitSignalBuffsChanged(); }
			return true;
		}
		AddActivation(new Buff { Name = condition.ResourceName, Condition = condition, Duration = 0 }, source);
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
			SetPersistentStatus(condition, source, true);
		}
	}

	/// <summary>Remove all conditions contributed by one source.</summary>
	public void RemoveStatuses(object source)
	{
		if (source != null && RemoveWhere(active => ReferenceEquals(active.Source, source))) EmitSignalBuffsChanged();
	}

	public void RemoveBuff(Buff buff)
	{
		var activeBuff = ActiveBuffs.FirstOrDefault(b => b.Buff == buff);
		if (activeBuff != null)
		{
			GD.Print($"{Player?.Name ?? Name} removed buff {buff.Name}");
			RemoveWhere(active => active == activeBuff);
			EmitSignalBuffsChanged();
		}
	}

	/// <summary>Removes every active buff, reversing each exactly once, e.g. before a saved player state is restored.</summary>
	public void ClearBuffs()
	{
		if (RemoveWhere(_ => true))
		{
			EmitSignalBuffsChanged();
		}
	}

	private void AddActivation(Buff buff, object source = null, double? remaining = null, double tickProgress = 0)
	{
		var active = new ActiveBuff();
		active.Initialize(Player, buff);
		active.SetSource(source);
		if (remaining.HasValue) active.RestoreClock(remaining.Value, tickProgress);
		ActiveBuffs.Add(active);
		AddChild(active);
		UpdateProcessing();
	}

	private void UpdateProcessing() => SetPhysicsProcess(ActiveBuffs.Any(active => !active.IsPermanent));

	/// <summary>
	/// Reverses and frees every active buff matching <paramref name="predicate"/>. Callers emit
	/// <see cref="BuffsChanged"/> once afterwards, so observers see one change per operation.
	/// </summary>
	private bool RemoveWhere(Func<ActiveBuff, bool> predicate)
	{
		bool removed = false;
		for (int i = ActiveBuffs.Count - 1; i >= 0; i--)
		{
			ActiveBuff activeBuff = ActiveBuffs[i];
			if (!predicate(activeBuff))
			{
				continue;
			}

			activeBuff.Deactivate();
			ActiveBuffs.RemoveAt(i);
			RemoveChild(activeBuff);
			activeBuff.QueueFree();
			removed = true;
		}

		if (removed) UpdateProcessing();
		return removed;
	}
}
