using Godot;

public enum LingeringAreaKind { SolarFlares, Hailstorm, IceWall }

/// <summary>Data for an area that remains active while its authored visual plays.</summary>
[GlobalClass]
public partial class LingeringAreaDefinition : Resource
{
	[Export] public string SpellName { get; set; } = "Lingering area";
	[Export] public LingeringAreaKind Kind { get; set; }
	[Export] public float[] RadiusByLevel { get; set; } = [3.5f, 5f, 6.5f];
	/// <summary>Damage per contact: per flame touch for Solar Flares, per shard for Hailstorm, per repel for Ice Wall.</summary>
	[Export] public float[] DamageByLevel { get; set; } = [3, 4, 5];
	[Export] public StatusCondition[] SlowConditions { get; set; } = [];
	[Export] public StatusCondition BurnCondition { get; set; }
	[Export] public float BurnDuration { get; set; } = 2;
	[Export] public float BurnTickSeconds { get; set; } = .5f;
	[Export] public float[] BurnDamageByLevel { get; set; } = [1, 1.5f, 2];
	[Export] public float FlameContactRadius { get; set; } = 1f;
	/// <summary>Hailstorm shards landing per second. They spread evenly over the area, so larger areas need more.</summary>
	[Export] public float[] ShardsPerSecondByLevel { get; set; } = [14, 26, 40];
	/// <summary>Hailstorm shard reach: actors this close to a landing point take that shard's hit.</summary>
	[Export] public float ShardHitRadius { get; set; } = 1f;
	/// <summary>Ice Wall: an enemy inside the ring is hit again at most this often while it stays.</summary>
	[Export] public float RepelSeconds { get; set; } = .8f;
	/// <summary>Ice Wall: how hard each hit shoves an enemy back out of the ring.</summary>
	[Export] public float RepelPush { get; set; } = 3f;
	public int LevelCount => System.Math.Min(3, System.Math.Min(RadiusByLevel?.Length ?? 0, DamageByLevel?.Length ?? 0));
	public bool IsValidLevel(int level)
	{
		int i = level - 1;
		return !string.IsNullOrWhiteSpace(SpellName) && System.Enum.IsDefined(Kind) && i >= 0 && i < LevelCount
			&& float.IsFinite(RadiusByLevel[i]) && RadiusByLevel[i] > 0
			&& float.IsFinite(DamageByLevel[i]) && DamageByLevel[i] > 0
			&& float.IsFinite(FlameContactRadius) && FlameContactRadius > 0
			&& (Kind != LingeringAreaKind.Hailstorm || SlowConditions != null && i < SlowConditions.Length && SlowConditions[i] != null
				&& ShardsPerSecondByLevel != null && i < ShardsPerSecondByLevel.Length && float.IsFinite(ShardsPerSecondByLevel[i])
				&& ShardsPerSecondByLevel[i] > 0 && float.IsFinite(ShardHitRadius) && ShardHitRadius > 0)
			&& (Kind != LingeringAreaKind.IceWall || float.IsFinite(RepelSeconds) && RepelSeconds > 0 && float.IsFinite(RepelPush) && RepelPush >= 0)
			&& (Kind != LingeringAreaKind.SolarFlares || BurnCondition != null && float.IsFinite(BurnDuration) && BurnDuration > 0
				&& float.IsFinite(BurnTickSeconds) && BurnTickSeconds > 0 && BurnDamageByLevel != null && i < BurnDamageByLevel.Length
				&& float.IsFinite(BurnDamageByLevel[i]) && BurnDamageByLevel[i] > 0);
	}
}
