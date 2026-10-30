using Godot;

public enum TargetDeliveryKind { Fireball, Traveling, Chain, Rockfall, Meteor }

/// <summary>Target count, damage and delivery policy for a focused attack.</summary>
[GlobalClass]
public partial class TargetedSpellDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Targeted spell";
	[Export] public TargetDeliveryKind Delivery { get; set; }
	[Export] public DamageElement Element { get; set; }
	[Export] public int[] TargetsByLevel { get; set; } = [1, 2, 3];
	/// <summary>Damage per hit as a multiple of the caster's weapon hit (see <see cref="SpellPower"/>).</summary>
	[Export] public float[] DamageScaleByLevel { get; set; } = [1, 1.375f, 1.75f];
	[Export] public float Radius { get; set; } = 9;
	[Export] public float HitRadius { get; set; } = .8f;
	public int LevelCount => System.Math.Min(SpellLevels.Max, System.Math.Min(TargetsByLevel?.Length ?? 0, DamageScaleByLevel?.Length ?? 0));
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& System.Enum.IsDefined(Delivery) && TargetsByLevel[i] > 0
			&& float.IsFinite(DamageScaleByLevel[i]) && DamageScaleByLevel[i] > 0
			&& float.IsFinite(Radius) && Radius > 0 && float.IsFinite(HitRadius) && HitRadius > 0;
	}
}
