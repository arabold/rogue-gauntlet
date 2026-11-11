using Godot;

/// <summary>Shared monster defaults. Scenes compose capabilities; components own live state.</summary>
[Tool]
[GlobalClass]
public partial class MonsterDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string DisplayName { get; set; } = "";
	[Export(PropertyHint.Range, "1,10000,1,or_greater")] public float MaxHealth { get; set; } = 10;
	[Export(PropertyHint.Range, "0,20,0.1,or_greater")] public float MoveSpeed { get; set; } = 3.2f;
	/// <summary>Base damage reduction before buffs; the hurtbox rolls it against every hit that does not bypass armor.</summary>
	[Export(PropertyHint.Range, "0,100,0.5,or_greater")] public float Armor { get; set; }
	[Export(PropertyHint.Range, "0,10000,1,or_greater")] public float MaxMana { get; set; }
	[Export(PropertyHint.Range, "0,50,0.1,or_greater")] public float ManaRegenPerSecond { get; set; }
	[Export(PropertyHint.Range, "0,10000,1,or_greater")] public int XpReward { get; set; }
	/// <summary>Optional creature vocalization layered on each accepted attack wind-up.</summary>
	[Export] public SoundCueProfile AttackVoiceCue { get; set; }
	/// <summary>Optional one-shot creature sound played when this monster dies.</summary>
	[Export] public SoundCueProfile DeathCue { get; set; }
	/// <summary>What the monster drops on death and how often.</summary>
	[Export] public LootDropProfile Loot { get; set; }
	[Export] public EnemyBehaviorProfile Behavior { get; set; }

	public void Validate(MonsterValidationContext context)
	{
		context.Check(!string.IsNullOrWhiteSpace(Id), "Definition.Id: enter a stable monster ID.");
		context.Check(!string.IsNullOrWhiteSpace(DisplayName), "Definition.DisplayName: enter a display name.");
		context.Check(MonsterValidationContext.IsPositive(MaxHealth), "Definition.MaxHealth: must be positive and finite.");
		context.Check(MonsterValidationContext.IsNonnegative(MoveSpeed), "Definition.MoveSpeed: must be nonnegative and finite.");
		context.Check(MonsterValidationContext.IsNonnegative(Armor), "Definition.Armor: must be nonnegative and finite.");
		context.Check(MonsterValidationContext.IsNonnegative(MaxMana) && MonsterValidationContext.IsNonnegative(ManaRegenPerSecond),
			"Definition.MaxMana and ManaRegenPerSecond must be nonnegative and finite.");
		context.Check(XpReward >= 0, "Definition.XpReward: cannot be negative.");
		if (context.Check(Behavior != null, "Definition.Behavior: assign a behavior Resource."))
			using (context.Scope("Behavior")) Behavior.Validate(context);
	}
}
