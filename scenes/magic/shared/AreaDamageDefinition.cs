using Godot;
using System.Collections.Generic;

/// <summary>Shared immutable contact values for an independently authored area spell.</summary>
[GlobalClass]
public partial class AreaDamageDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Area spell";
	[Export] public float[] RadiusByLevel { get; set; } = [3.5f, 5f, 6.5f];
	/// <summary>Damage per contact as a multiple of the caster's weapon hit (see <see cref="SpellPower"/>).</summary>
	[Export] public float[] DamageScaleByLevel { get; set; } = [.375f, .4125f, .45f];
	/// <summary>Initial outward push speed per level, shoving actors away from the cast point; empty does not push.</summary>
	[Export] public float[] PushByLevel { get; set; } = [];
	/// <summary>Seconds between the visual's release and its contact, for delayed eruptions.</summary>
	[Export(PropertyHint.Range, "0,10,0.05")] public float ContactDelay { get; set; }
	/// <summary>Optional camera shake strength requested when this area spell reaches a target.</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float CameraImpulse { get; set; }
	/// <summary>Horizontal radius over which the area spell's camera shake fades out.</summary>
	[Export(PropertyHint.Range, "0,30,0.1")] public float CameraRadius { get; set; } = 11;
	/// <summary>Seconds for the camera shake to reach full strength.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float CameraGrowthTime { get; set; }
	/// <summary>Seconds the camera shake holds at full strength before fading.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float CameraDuration { get; set; }
	/// <summary>Seconds for the camera shake to fade out.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float CameraDecayTime { get; set; } = .18f;
	[Export] public AttackDefenseTraits DefenseTraits { get; set; } = AttackDefenseTraits.Dodgeable | AttackDefenseTraits.Guardable;
	[Export] public DamageElement Element { get; set; }
	public int LevelCount => System.Math.Min(SpellLevels.Max, System.Math.Min(RadiusByLevel?.Length ?? 0, DamageScaleByLevel?.Length ?? 0));
	public float PushAt(int level) => PushByLevel is { Length: > 0 } ? PushByLevel[level - 1] : 0;
	public virtual bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && i >= 0 && i < LevelCount
			&& float.IsFinite(RadiusByLevel[i]) && RadiusByLevel[i] > 0
			&& float.IsFinite(DamageScaleByLevel[i]) && DamageScaleByLevel[i] > 0
			&& (PushByLevel is not { Length: > 0 } || i < PushByLevel.Length && float.IsFinite(PushByLevel[i]) && PushByLevel[i] >= 0)
			&& float.IsFinite(ContactDelay) && ContactDelay >= 0
			&& float.IsFinite(CameraImpulse) && CameraImpulse >= 0
			&& (CameraImpulse == 0 || float.IsFinite(CameraRadius) && CameraRadius > 0
				&& float.IsFinite(CameraGrowthTime) && CameraGrowthTime >= 0
				&& float.IsFinite(CameraDuration) && CameraDuration >= 0
				&& float.IsFinite(CameraDecayTime) && CameraDecayTime > 0);
	}

	/// <summary>Damage per contact.</summary>
	public virtual IEnumerable<string> DescribeLevel(in SpellPower power) => !IsValidLevel(power.Level) ? []
		: [SpellLevels.Damage(power.Hit.Scaled(DamageScaleByLevel[power.Level - 1])) + " per hit"];
}
