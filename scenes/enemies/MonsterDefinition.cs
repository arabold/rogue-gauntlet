using Godot;

/// <summary>Shared monster defaults. Scenes compose capabilities; components own live state.</summary>
[GlobalClass]
public partial class MonsterDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string DisplayName { get; set; } = "";
	[Export(PropertyHint.Range, "1,10000,1,or_greater")] public float MaxHealth { get; set; } = 10;
	[Export(PropertyHint.Range, "0,20,0.1,or_greater")] public float MoveSpeed { get; set; } = 3;
	[Export(PropertyHint.Range, "0,10000,1,or_greater")] public int XpReward { get; set; }
	[Export] public LootTable LootTable { get; set; }
	[Export] public EnemyBehaviorProfile Behavior { get; set; }
}