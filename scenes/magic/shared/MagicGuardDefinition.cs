using Godot;

/// <summary>Immutable per-level magic guard capacity and reflection tuning.</summary>
[GlobalClass]
public partial class MagicGuardDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Magic Guard";
	[Export] public DamageElement Element { get; set; }
	[Export] public float[] CapacityByLevel { get; set; } = [20, 30, 40];
	[Export] public float[] ReflectionByLevel { get; set; } = [.35f, .5f, .65f];
	/// <summary>How much fire damage each point of shield capacity absorbs.</summary>
	[Export(PropertyHint.Range, "1,4,0.05")] public float FireAbsorptionEfficiency { get; set; } = 1;
	public int LevelCount => System.Math.Min(3, System.Math.Min(CapacityByLevel?.Length ?? 0, ReflectionByLevel?.Length ?? 0));
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& float.IsFinite(CapacityByLevel[i]) && CapacityByLevel[i] > 0
			&& float.IsFinite(ReflectionByLevel[i]) && ReflectionByLevel[i] >= 0
			&& float.IsFinite(FireAbsorptionEfficiency) && FireAbsorptionEfficiency >= 1;
	}
}
