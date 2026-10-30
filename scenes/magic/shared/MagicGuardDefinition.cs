using Godot;
using System.Collections.Generic;

/// <summary>Immutable per-level magic guard capacity and reflection tuning.</summary>
[GlobalClass]
public partial class MagicGuardDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Magic Guard";
	[Export] public DamageElement Element { get; set; }
	/// <summary>Damage the guard absorbs, as a multiple of the caster's average hit at cast time.</summary>
	[Export] public float[] CapacityScaleByLevel { get; set; } = [2.5f, 2.75f, 3];
	[Export] public float[] ReflectionByLevel { get; set; } = [.35f, .5f, .65f];
	public int LevelCount => System.Math.Min(SpellLevels.Max, System.Math.Min(CapacityScaleByLevel?.Length ?? 0, ReflectionByLevel?.Length ?? 0));
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& float.IsFinite(CapacityScaleByLevel[i]) && CapacityScaleByLevel[i] > 0
			&& float.IsFinite(ReflectionByLevel[i]) && ReflectionByLevel[i] >= 0;
	}

	/// <summary>How much damage the guard absorbs and how much of it reflects.</summary>
	public IEnumerable<string> DescribeLevel(in SpellPower power) => !IsValidLevel(power.Level) ? []
		: [$"Absorbs {SpellLevels.Points(CapacityScaleByLevel[power.Level - 1] * power.Hit.Average)} damage",
			$"Reflects {Mathf.RoundToInt(ReflectionByLevel[power.Level - 1] * 100)}%"];
}
