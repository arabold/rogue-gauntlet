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
	[Export] public float[] DamageByLevel { get; set; } = [8, 11, 14];
	[Export] public float Radius { get; set; } = 9;
	[Export] public float HitRadius { get; set; } = .8f;
	public int LevelCount => System.Math.Min(3, System.Math.Min(TargetsByLevel?.Length ?? 0, DamageByLevel?.Length ?? 0));
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& System.Enum.IsDefined(Delivery) && TargetsByLevel[i] > 0
			&& float.IsFinite(DamageByLevel[i]) && DamageByLevel[i] > 0
			&& float.IsFinite(Radius) && Radius > 0 && float.IsFinite(HitRadius) && HitRadius > 0;
	}
}
