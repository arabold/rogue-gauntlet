using Godot;
using Godot.Collections;
using System.Linq;

/// <summary>
/// Owns runtime buff instances applied to a player and tracks the <see cref="StatusCondition"/>s they impose.
/// A condition is active while at least one active buff carries it, so overlapping sources (poisoned food
/// followed by a poison potion) report one continuous condition.
/// </summary>
public partial class BuffController : Node
{
	/// <summary>
	/// Fired when the first active buff carrying <paramref name="condition"/> is applied, after its
	/// <see cref="Buff.OnApply"/> ran, so <see cref="HasCondition"/> is already true in handlers.
	/// </summary>
	[Signal] public delegate void ConditionGainedEventHandler(StatusCondition condition);

	/// <summary>
	/// Fired when the last active buff carrying <paramref name="condition"/> expires or is removed, after its
	/// <see cref="Buff.OnRemove"/> ran, so <see cref="HasCondition"/> is already false in handlers.
	/// </summary>
	[Signal] public delegate void ConditionLostEventHandler(StatusCondition condition);

	private readonly System.Collections.Generic.Dictionary<StatusCondition, int> _conditionCounts = [];

	[Export] public Player Player { get; set; }

	public Array<ActiveBuff> ActiveBuffs { get; } = new();

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
	}

	public override void _PhysicsProcess(double delta)
	{
		for (int i = ActiveBuffs.Count - 1; i >= 0; i--)
		{
			if (ActiveBuffs[i].IsExpired)
				RemoveActiveBuff(ActiveBuffs[i]);
		}
	}

	/// <summary>Whether any active buff currently imposes <paramref name="condition"/>.</summary>
	public bool HasCondition(StatusCondition condition)
	{
		return condition != null && _conditionCounts.ContainsKey(condition);
	}

	public void ApplyBuff(Buff buff)
	{
		GD.Print($"{Player?.Name ?? Name} applied buff {buff.Name}");
		var activeBuff = new ActiveBuff();
		activeBuff.Initialize(Player, buff);
		ActiveBuffs.Add(activeBuff);

		AddChild(activeBuff);
		AcquireCondition(buff.Condition);
	}

	public void RemoveBuff(Buff buff)
	{
		var activeBuff = ActiveBuffs.FirstOrDefault(b => b.Buff == buff);
		if (activeBuff != null)
		{
			GD.Print($"{Player?.Name ?? Name} removed buff {buff.Name}");
			RemoveActiveBuff(activeBuff);
		}
	}

	/// <summary>
	/// Removes every active buff, reversing each exactly once and reporting each condition that ends, e.g. before
	/// a saved player state is restored.
	/// </summary>
	public void ClearBuffs()
	{
		for (int i = ActiveBuffs.Count - 1; i >= 0; i--)
		{
			RemoveActiveBuff(ActiveBuffs[i]);
		}
	}

	private void RemoveActiveBuff(ActiveBuff activeBuff)
	{
		activeBuff.Deactivate();
		ActiveBuffs.Remove(activeBuff);
		RemoveChild(activeBuff);
		activeBuff.QueueFree();
		ReleaseCondition(activeBuff.Buff.Condition);
	}

	private void AcquireCondition(StatusCondition condition)
	{
		if (condition == null)
		{
			return;
		}

		_conditionCounts.TryGetValue(condition, out int count);
		_conditionCounts[condition] = count + 1;
		if (count == 0)
		{
			EmitSignalConditionGained(condition);
		}
	}

	private void ReleaseCondition(StatusCondition condition)
	{
		if (condition == null || !_conditionCounts.TryGetValue(condition, out int count))
		{
			return;
		}

		if (count > 1)
		{
			_conditionCounts[condition] = count - 1;
			return;
		}

		_conditionCounts.Remove(condition);
		EmitSignalConditionLost(condition);
	}
}