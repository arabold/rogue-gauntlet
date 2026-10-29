using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Serializes live player activations without serializing Godot objects or replaying one-time cures.</summary>
public static class BuffSaveCodec
{
	public sealed record Restored(Buff Buff, object Source, double Remaining, double TickProgress);

	public static List<ActiveBuffSaveData> Capture(Player player)
	{
		var equipped = player.Inventory.EquippedItems.Values.Where(slot => slot?.Item is EquipableItem)
			.Select(slot => ((EquipableItem)slot.Item).Buff).Where(buff => buff != null).ToHashSet();
		var saved = new List<ActiveBuffSaveData>();
		foreach (var active in player.BuffController.ActiveBuffs)
		{
			if (active.IsExpired) continue;
			if (equipped.Contains(active.Buff))
			{
				if (!active.IsPermanent) throw new InvalidOperationException("Timed equipment buffs cannot yet be saved safely.");
				continue; // Equipment restores its own activation when re-equipped.
			}
			var buff = active.Buff;
			var record = new ActiveBuffSaveData { Name = buff.Name ?? "", Permanent = active.IsPermanent,
				Remaining = active.IsPermanent ? 0 : active.RemainingDuration, TickProgress = active.TickProgress,
				ConditionPath = buff.Condition?.ResourcePath ?? "" };
			if (buff.Condition != null && (string.IsNullOrEmpty(record.ConditionPath) || record.ConditionPath.Contains("::")))
				throw new InvalidOperationException("Cannot save a condition without an external resource identity.");
			if (active.Source != null)
			{
				if (active.Source is not Node node || !GodotObject.IsInstanceValid(node) || !IsAuthoredSource(player, node))
					throw new InvalidOperationException("Cannot save a status whose source cannot be restored with the player.");
				record.HasSource = true; record.SourcePath = player.GetPathTo(node).ToString();
			}
			switch (buff)
			{
				case HealingBuff healing: record.Kind = "healing"; record.TicksPerSecond = healing.TicksPerSecond; record.PointsPerTick = healing.HealthPointsPerTick; break;
				case PoisonBuff poison: record.Kind = "poison"; record.TicksPerSecond = poison.TicksPerSecond; record.PointsPerTick = poison.DamagePointsPerTick; break;
				case TimeScaleBuff time: record.Kind = "time_scale"; record.TimeScale = time.TimeScale; break;
				case StatModifierBuff stat: record.Kind = "stat_modifier"; record.Modifiers = ItemSaveCodec.CaptureModifiers(stat.Modifiers); break;
				case Buff plain when plain.GetType() == typeof(Buff): record.Kind = "condition"; break;
				default: throw new InvalidOperationException($"Cannot save unsupported buff type {buff.GetType().Name}.");
			}
			saved.Add(record);
		}
		Prepare(player, saved); // Never write a snapshot that this version would reject on load.
		return saved;
	}

	private static bool IsAuthoredSource(Player player, Node source)
	{
		for (Node owner = source; owner != null; owner = owner.Owner)
			if (owner == player) return true;
		return false;
	}

	/// <summary>Resolve and validate everything before clearing the player's existing state.</summary>
	public static List<Restored> Prepare(Player player, List<ActiveBuffSaveData> saved)
	{
		if (saved == null) throw new InvalidOperationException("Missing saved player buffs.");
		var result = new List<Restored>();
		foreach (var data in saved)
		{
			if (data == null || !double.IsFinite(data.Remaining) || data.Remaining < 0 || data.Remaining > float.MaxValue
				|| (!data.Permanent && data.Remaining <= 0) || (data.Permanent && data.Remaining != 0)
				|| !double.IsFinite(data.TickProgress) || data.TickProgress < 0)
				throw new InvalidOperationException("Invalid saved buff clock.");
			object source = null;
			if (data.HasSource)
			{
				if (string.IsNullOrEmpty(data.SourcePath) || data.SourcePath.StartsWith('/') || data.SourcePath.Contains(".."))
					throw new InvalidOperationException("Invalid saved status source.");
				source = player.GetNodeOrNull(data.SourcePath) ?? throw new InvalidOperationException("Saved status source is missing.");
			}
			else if (!string.IsNullOrEmpty(data.SourcePath)) throw new InvalidOperationException("Invalid saved status source.");
			StatusCondition condition = null;
			if (!string.IsNullOrEmpty(data.ConditionPath))
				condition = ResourceLoader.Load<StatusCondition>(data.ConditionPath) ?? throw new InvalidOperationException("Saved status condition is missing.");
			Buff buff = data.Kind switch
			{
				"condition" => new Buff(),
				"healing" => new HealingBuff { TicksPerSecond = data.TicksPerSecond, HealthPointsPerTick = data.PointsPerTick },
				"poison" => new PoisonBuff { TicksPerSecond = data.TicksPerSecond, DamagePointsPerTick = data.PointsPerTick },
				"time_scale" => new TimeScaleBuff { TimeScale = data.TimeScale },
				"stat_modifier" => new StatModifierBuff { Modifiers = ItemSaveCodec.BuildModifiers(data.Modifiers) },
				_ => throw new InvalidOperationException("Unsupported saved buff type."),
			};
			if (buff is PeriodicBuff periodic && (!float.IsFinite(periodic.TicksPerSecond) || periodic.TicksPerSecond < 0
				|| (periodic.TicksPerSecond > 0 && data.TickProgress >= 1.0 / periodic.TicksPerSecond)))
				throw new InvalidOperationException("Invalid saved periodic buff clock.");
			if (buff is not PeriodicBuff && data.TickProgress != 0 || buff is TimeScaleBuff time && (!float.IsFinite(time.TimeScale) || time.TimeScale <= 0))
				throw new InvalidOperationException("Invalid saved buff settings.");
			buff.Name = data.Name; buff.Condition = condition; buff.Duration = data.Permanent ? 0 : (float)data.Remaining;
			result.Add(new Restored(buff, source, data.Remaining, data.TickProgress));
		}
		return result;
	}
}
