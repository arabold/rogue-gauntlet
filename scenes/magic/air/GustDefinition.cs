using Godot;

/// <summary>Authored Gust radius, damage and push strength; casts never mutate this shared definition.</summary>
[GlobalClass]
public partial class GustDefinition : Resource
{
	[Export] public float[] RadiusByLevel { get; set; } = [3.5f, 5f, 6.5f];
	[Export] public float[] DamageByLevel { get; set; } = [3f, 4f, 5f];
	/// <summary>Initial push speed; the target's movement speed controls how quickly it decays.</summary>
	[Export] public float[] PushByLevel { get; set; } = [2f, 2.4f, 2.8f];
	/// <summary>Seconds between release and the area contact.</summary>
	[Export(PropertyHint.Range, "0,10,0.05")] public float ContactDelay { get; set; }
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;

	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return level <= 3 && i >= 0 && RadiusByLevel != null && DamageByLevel != null && PushByLevel != null
			&& i < RadiusByLevel.Length && i < DamageByLevel.Length && i < PushByLevel.Length
			&& float.IsFinite(RadiusByLevel[i]) && RadiusByLevel[i] > 0
			&& float.IsFinite(DamageByLevel[i]) && DamageByLevel[i] > 0
			&& float.IsFinite(PushByLevel[i]) && PushByLevel[i] > 0
			&& float.IsFinite(ContactDelay) && ContactDelay >= 0;
	}
}
