using Godot;
using System.Collections.Generic;

public enum TargetDeliveryKind { Fireball, Traveling, Chain, Rockfall, Meteor }
public enum SpellTargetingKind { Forward, Area }

/// <summary>Target count, damage and delivery policy for a focused attack.</summary>
[GlobalClass]
public partial class TargetedSpellDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Targeted spell";
	[Export] public TargetDeliveryKind Delivery { get; set; }
	[Export] public SpellTargetingKind Targeting { get; set; } = SpellTargetingKind.Forward;
	[Export] public bool CanTargetSameEnemy { get; set; } = false;
	[Export] public DamageElement Element { get; set; }
	[Export] public int[] TargetsByLevel { get; set; } = [1, 2, 3];
	/// <summary>Damage per hit as a multiple of the caster's weapon hit (see <see cref="SpellPower"/>).</summary>
	[Export] public float[] DamageScaleByLevel { get; set; } = [1, 1.1f, 1.2f];
	/// <summary>Reach of the spell; for forward-facing spells this defaults to 16 matching magic missiles.</summary>
	[Export] public float Radius { get; set; } = 16f;
	[Export] public float HitRadius { get; set; } = .8f;
	/// <summary>Half-cone angle in degrees for forward target acquisition (same as magic missiles: 55°).</summary>
	[Export] public float AimingAngle { get; set; } = 55f;
	/// <summary>Total fan angle in degrees for random scatter of excess forward projectiles (same as missile fan: 60°).</summary>
	[Export] public float SpreadAngle { get; set; } = 60f;
	public int LevelCount => System.Math.Min(SpellLevels.Max, System.Math.Min(TargetsByLevel?.Length ?? 0, DamageScaleByLevel?.Length ?? 0));
	public bool IsAreaTargeting => Targeting == SpellTargetingKind.Area || Delivery is TargetDeliveryKind.Rockfall or TargetDeliveryKind.Meteor;
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& System.Enum.IsDefined(Delivery) && System.Enum.IsDefined(Targeting) && TargetsByLevel[i] > 0
			&& float.IsFinite(DamageScaleByLevel[i]) && DamageScaleByLevel[i] > 0
			&& float.IsFinite(Radius) && Radius > 0 && float.IsFinite(HitRadius) && HitRadius > 0
			&& float.IsFinite(AimingAngle) && AimingAngle > 0 && float.IsFinite(SpreadAngle) && SpreadAngle >= 0;
	}

	/// <summary>Damage per hit.</summary>
	public IEnumerable<string> DescribeLevel(in SpellPower power) => !IsValidLevel(power.Level) ? []
		: [SpellLevels.Damage(power.Hit.Scaled(DamageScaleByLevel[power.Level - 1])) + " per hit"];
}
