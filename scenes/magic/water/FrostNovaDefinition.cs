using Godot;

/// <summary>Authored Frost Nova contact values plus the freeze it applies; casts never mutate the shared definition.</summary>
[GlobalClass]
public partial class FrostNovaDefinition : AreaDamageDefinition
{
	[Export] public float[] FreezeSecondsByLevel { get; set; } = [1.5f, 2f, 2.5f];
	[Export] public StatusCondition Frozen { get; set; }

	public override bool IsValidLevel(int level)
	{
		int i = level - 1;
		return base.IsValidLevel(level) && Frozen != null && FreezeSecondsByLevel != null && i < FreezeSecondsByLevel.Length
			&& float.IsFinite(FreezeSecondsByLevel[i]) && FreezeSecondsByLevel[i] > 0;
	}
}
