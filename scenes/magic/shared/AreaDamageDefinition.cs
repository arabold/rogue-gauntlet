using Godot;

/// <summary>Shared immutable contact values for an independently authored area spell.</summary>
[GlobalClass]
public partial class AreaDamageDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Area spell";
	[Export] public float[] RadiusByLevel { get; set; } = [3.5f, 5f, 6.5f];
	[Export] public float[] DamageByLevel { get; set; } = [3f, 4f, 5f];
	/// <summary>Initial outward push speed per level, shoving actors away from the cast point; empty does not push.</summary>
	[Export] public float[] PushByLevel { get; set; } = [];
	/// <summary>Seconds between the visual's release and its contact, for delayed eruptions.</summary>
	[Export(PropertyHint.Range, "0,10,0.05")] public float ContactDelay { get; set; }
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;
	[Export] public DamageElement Element { get; set; }
	public int LevelCount => System.Math.Min(3, System.Math.Min(RadiusByLevel?.Length ?? 0, DamageByLevel?.Length ?? 0));
	public float PushAt(int level) => PushByLevel is { Length: > 0 } ? PushByLevel[level - 1] : 0;
	public virtual bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& float.IsFinite(RadiusByLevel[i]) && RadiusByLevel[i] > 0
			&& float.IsFinite(DamageByLevel[i]) && DamageByLevel[i] > 0
			&& (PushByLevel is not { Length: > 0 } || i < PushByLevel.Length && float.IsFinite(PushByLevel[i]) && PushByLevel[i] >= 0)
			&& float.IsFinite(ContactDelay) && ContactDelay >= 0;
	}
}
