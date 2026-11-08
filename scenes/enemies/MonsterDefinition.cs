using Godot;

/// <summary>Shared monster defaults. Scenes compose capabilities; components own live state.</summary>
[GlobalClass]
public partial class MonsterDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string DisplayName { get; set; } = "";
	[Export(PropertyHint.Range, "1,10000,1,or_greater")] public float MaxHealth { get; set; } = 10;
	[Export(PropertyHint.Range, "0,20,0.1,or_greater")] public float MoveSpeed { get; set; } = 3.2f;
	[Export(PropertyHint.Range, "0,10000,1,or_greater")] public int XpReward { get; set; }
	/// <summary>Optional creature vocalization layered on each accepted attack wind-up.</summary>
	[Export] public SoundCueProfile AttackVoiceCue { get; set; }
	/// <summary>Optional one-shot creature sound played when this monster dies.</summary>
	[Export] public SoundCueProfile DeathCue { get; set; }
	/// <summary>What the monster drops on death and how often.</summary>
	[Export] public LootDropProfile Loot { get; set; }
	[Export] public EnemyBehaviorProfile Behavior { get; set; }
}