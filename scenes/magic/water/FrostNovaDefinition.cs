using Godot;

/// <summary>Authored Frost Nova contact values; casts never mutate the shared definition.</summary>
[GlobalClass]
public partial class FrostNovaDefinition : Resource
{
	[Export] public float[] RadiusByLevel { get; set; } = [3.5f, 5f, 6.5f];
	[Export] public float[] DamageByLevel { get; set; } = [3f, 4f, 5f];
	[Export] public float[] FreezeSecondsByLevel { get; set; } = [1.5f, 2f, 2.5f];
	[Export] public StatusCondition Frozen { get; set; }
	/// <summary>Seconds between release and the area contact.</summary>
	[Export(PropertyHint.Range, "0,10,0.05")] public float ContactDelay { get; set; }
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;

	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return level <= 3 && Frozen != null && RadiusByLevel != null && DamageByLevel != null && FreezeSecondsByLevel != null
			&& i >= 0 && i < RadiusByLevel.Length && i < DamageByLevel.Length && i < FreezeSecondsByLevel.Length
			&& float.IsFinite(RadiusByLevel[i]) && RadiusByLevel[i] > 0
			&& float.IsFinite(DamageByLevel[i]) && DamageByLevel[i] > 0
			&& float.IsFinite(FreezeSecondsByLevel[i]) && FreezeSecondsByLevel[i] > 0
			&& float.IsFinite(ContactDelay) && ContactDelay >= 0;
	}
}
