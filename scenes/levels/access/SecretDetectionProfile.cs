using Godot;
using System;

/// <summary>Shared detection tuning; runtime clocks and random state belong to each <see cref="ProximityDiscoveryComponent"/>.</summary>
[Tool]
[GlobalClass]
public partial class SecretDetectionProfile : Resource
{
	[Export] public StatType DetectionStat { get; set; } = StatType.Intelligence;
	[Export] public float Interval { get; set; } = 0.5f;
	[Export] public float Radius { get; set; } = 6;
	[Export] public float VerticalTolerance { get; set; } = 2;
	[Export] public float BaseChance { get; set; } = 0.1f;
	[Export] public float ReferenceStat { get; set; } = 10;
	[Export] public float ChancePerStat { get; set; } = 0.01f;
	[Export] public float MaximumChance { get; set; } = 0.9f;
	public float Chance(float stat) => Mathf.Clamp(BaseChance + (stat - ReferenceStat) * ChancePerStat, 0, MaximumChance);
	public void Validate()
	{
		if (DetectionStat is not (StatType.Strength or StatType.Dexterity or StatType.Vitality or StatType.Intelligence) || !float.IsFinite(Interval) || Interval <= 0 || !float.IsFinite(Radius) || Radius <= 0
			|| !float.IsFinite(VerticalTolerance) || VerticalTolerance < 0 || !float.IsFinite(ReferenceStat)
			|| !float.IsFinite(ChancePerStat) || ChancePerStat < 0 || !float.IsFinite(BaseChance) || BaseChance < 0 || BaseChance > 1
			|| !float.IsFinite(MaximumChance) || MaximumChance < 0 || MaximumChance > 1)
			throw new InvalidOperationException($"Invalid secret detection profile: {ResourcePath}");
	}
	public bool InRange(Vector3 observer, Vector3 target) => Math.Abs(observer.Y - target.Y) <= VerticalTolerance
		&& new Vector2(observer.X - target.X, observer.Z - target.Z).Length() <= Radius;
	public float Resolve(PlayerStats stats) => DetectionStat switch
	{
		StatType.Strength => stats.Strength, StatType.Dexterity => stats.Dexterity,
		StatType.Vitality => stats.Vitality, StatType.Intelligence => stats.Intelligence,
		_ => throw new InvalidOperationException("Secret detection currently supports primary attributes.")
	};
}