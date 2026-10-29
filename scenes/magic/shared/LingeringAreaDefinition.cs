using Godot;

public enum LingeringAreaKind { SolarFlares, Snowstorm }

/// <summary>Data for an area that remains active while its authored visual plays.</summary>
[GlobalClass]
public partial class LingeringAreaDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Lingering area";
	[Export] public LingeringAreaKind Kind { get; set; }
	[Export] public float[] RadiusByLevel { get; set; } = [3.5f, 5f, 6.5f];
	[Export] public float[] DamageByLevel { get; set; } = [3, 4, 5];
	[Export] public StatusCondition[] SlowConditions { get; set; } = [];
	[Export] public StatusCondition BurnCondition { get; set; }
	[Export] public float BurnDuration { get; set; } = 2;
	[Export] public float BurnTickSeconds { get; set; } = .5f;
	[Export] public float[] BurnDamageByLevel { get; set; } = [1, 1.5f, 2];
	[Export] public float FlameContactRadius { get; set; } = 1f;
	public int LevelCount => System.Math.Min(3, System.Math.Min(RadiusByLevel?.Length ?? 0, DamageByLevel?.Length ?? 0));
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && System.Enum.IsDefined(Kind) && i >= 0 && i < LevelCount
			&& float.IsFinite(RadiusByLevel[i]) && RadiusByLevel[i] > 0
			&& float.IsFinite(DamageByLevel[i]) && DamageByLevel[i] > 0
			&& float.IsFinite(FlameContactRadius) && FlameContactRadius > 0
			&& (Kind != LingeringAreaKind.Snowstorm || SlowConditions != null && i < SlowConditions.Length && SlowConditions[i] != null)
			&& (Kind != LingeringAreaKind.SolarFlares || BurnCondition != null && float.IsFinite(BurnDuration) && BurnDuration > 0
				&& float.IsFinite(BurnTickSeconds) && BurnTickSeconds > 0 && BurnDamageByLevel != null && i < BurnDamageByLevel.Length
				&& float.IsFinite(BurnDamageByLevel[i]) && BurnDamageByLevel[i] > 0);
	}
}
