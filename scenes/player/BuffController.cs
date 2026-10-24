using Godot;
using Godot.Collections;
using System;
using System.Linq;

/// <summary>
/// Owns runtime buff instances applied to a player. A buff may impose a <see cref="StatusCondition"/>; the
/// condition is active while at least one active buff carries it, so overlapping sources (poisoned food
/// followed by a poison potion) read as one continuous condition. Observers re-read what they need on
/// <see cref="BuffsChanged"/>.
/// </summary>
public partial class BuffController : Node
{
	/// <summary>Fired once after buffs were applied, removed, expired or cleared.</summary>
	[Signal] public delegate void BuffsChangedEventHandler();

	[Export] public Player Player { get; set; }

	public Array<ActiveBuff> ActiveBuffs { get; } = new();

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
	}

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

	/// <summary>Applies <paramref name="buff"/>, first ending the conditions it cures (e.g. healing ends blindness).</summary>
	public void ApplyBuff(Buff buff)
	{
		GD.Print($"{Player?.Name ?? Name} applied buff {buff.Name}");
		RemoveWhere(active => active.Buff.Condition != null && buff.CuredConditions.Contains(active.Buff.Condition));
		var activeBuff = new ActiveBuff();
		activeBuff.Initialize(Player, buff);
		ActiveBuffs.Add(activeBuff);

		AddChild(activeBuff);
		EmitSignalBuffsChanged();
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

		return removed;
	}
}