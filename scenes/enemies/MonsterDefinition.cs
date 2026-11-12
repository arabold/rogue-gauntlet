using Godot;

/// <summary>Shared monster defaults. Scenes compose capabilities; components own live state.</summary>
[Tool]
[GlobalClass]
public partial class MonsterDefinition : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string DisplayName { get; set; } = "";

	[ExportGroup("Balance")]
	/// <summary>The combat role the balance model sizes this monster's stats for.</summary>
	[Export] public MonsterArchetype Archetype { get; set; }
	/// <summary>
	/// The dungeon depth the <see cref="Archetype"/>'s stats are sized for. The pair is the balance model's authoring
	/// input: <c>simulate.py derive</c> writes the health, armor, speed and attack values from it, and
	/// <c>simulate.py audit</c> reports values that drifted.
	/// </summary>
	[Export(PropertyHint.Range, "1,30,0.5,or_greater")] public float BalanceDepth { get; set; } = 1;
	/// <summary>
	/// The depth at which this monster is exactly one standard fight, computed by the balance model from the stats and
	/// written by <c>simulate.py derive</c>; read-only in the Inspector. A brute sized for a depth is a harder fight
	/// there, so its threat level is above its <see cref="BalanceDepth"/>; a lone swarmer is far easier, so its level
	/// can fall below 1 or even 0 (the scale is logarithmic in fight cost).
	/// </summary>
	[Export(PropertyHint.Range, "-20,40,0.01,or_greater")] public float ThreatLevel { get; set; } = 1;

	[ExportGroup("Stats")]
	[Export(PropertyHint.Range, "1,10000,1,or_greater")] public float MaxHealth { get; set; } = 10;
	[Export(PropertyHint.Range, "0,20,0.1,or_greater")] public float MoveSpeed { get; set; } = 3.2f;
	/// <summary>Base damage reduction before buffs; the hurtbox rolls it against every hit that does not bypass armor.</summary>
	[Export(PropertyHint.Range, "0,100,0.5,or_greater")] public float Armor { get; set; }
	[Export(PropertyHint.Range, "0,10000,1,or_greater")] public float MaxMana { get; set; }
	[Export(PropertyHint.Range, "0,50,0.1,or_greater")] public float ManaRegenPerSecond { get; set; }
	/// <summary>What the monster drops on death and how often.</summary>
	[Export] public LootDropProfile Loot { get; set; }
	[Export] public EnemyBehaviorProfile Behavior { get; set; }

	/// <summary>The balance model owns <see cref="ThreatLevel"/>, so the Inspector shows it without letting it be edited.</summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (property["name"].AsStringName() == PropertyName.ThreatLevel)
			property["usage"] = (int)(property["usage"].As<PropertyUsageFlags>() | PropertyUsageFlags.ReadOnly);
	}

	public void Validate(MonsterValidationContext context)
	{
		context.Check(!string.IsNullOrWhiteSpace(Id), "Definition.Id: enter a stable monster ID.");
		context.Check(!string.IsNullOrWhiteSpace(DisplayName), "Definition.DisplayName: enter a display name.");
		context.Check(MonsterValidationContext.IsPositive(BalanceDepth), "Definition.BalanceDepth: must be positive and finite.");
		context.Check(MonsterValidationContext.IsPositive(MaxHealth), "Definition.MaxHealth: must be positive and finite.");
		context.Check(MonsterValidationContext.IsNonnegative(MoveSpeed), "Definition.MoveSpeed: must be nonnegative and finite.");
		context.Check(MonsterValidationContext.IsNonnegative(Armor), "Definition.Armor: must be nonnegative and finite.");
		context.Check(MonsterValidationContext.IsNonnegative(MaxMana) && MonsterValidationContext.IsNonnegative(ManaRegenPerSecond),
			"Definition.MaxMana and ManaRegenPerSecond must be nonnegative and finite.");
		if (context.Check(Behavior != null, "Definition.Behavior: assign a behavior Resource."))
			using (context.Scope("Behavior")) Behavior.Validate(context);
	}
}
