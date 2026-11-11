using Godot;

/// <summary>Exclusive reactions to one observed threat; controllers retain timers, RNG and guard state.</summary>
[Tool]
[GlobalClass]
public partial class EnemyDefenseStrategy : Resource
{
	[Export(PropertyHint.Range, "0,1,0.01")] public float GuardChance { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float DodgeChance { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float RiposteChance { get; set; } = .65f;
	[Export] public float ReactionDelay { get; set; } = .04f;
	[Export] public float GuardPressureWindow { get; set; } = .85f;
	[Export] public float MaxGuardDuration { get; set; } = 3.5f;
	[Export] public bool CanDefendProjectiles { get; set; } = true;

	/// <summary>
	/// Unavailable capabilities leave their interval as no response; never reroll. When the observed attack is
	/// not safe to guard (not Guardable, or BreaksGuard), the guard interval dodges instead if the actor can.
	/// </summary>
	public EnemyDefenseMode Choose(bool canGuard, bool canDodge, float roll, bool guardSafe = true)
	{
		if (roll < DodgeChance) return canDodge ? EnemyDefenseMode.Dodge : EnemyDefenseMode.None;
		if (roll < DodgeChance + GuardChance)
			return guardSafe ? (canGuard ? EnemyDefenseMode.GuardParry : EnemyDefenseMode.None)
				: canDodge ? EnemyDefenseMode.Dodge : EnemyDefenseMode.None;
		return EnemyDefenseMode.None;
	}

	/// <summary>Probabilities and timings, plus the wired guard/dodge capabilities and clips that nonzero chances need.</summary>
	public void Validate(MonsterValidationContext context)
	{
		context.Check(MonsterValidationContext.IsProbability(GuardChance) && MonsterValidationContext.IsProbability(DodgeChance)
			&& MonsterValidationContext.IsProbability(RiposteChance) && GuardChance + DodgeChance <= 1,
			"DefenseStrategy: probabilities must be in [0,1], with guard + dodge <= 1.");
		context.Check(MonsterValidationContext.IsNonnegative(ReactionDelay) && MonsterValidationContext.IsPositive(GuardPressureWindow)
			&& MonsterValidationContext.IsPositive(MaxGuardDuration), "DefenseStrategy: invalid delay or guard durations.");
		ValidateCapabilities(context);
	}

	private void ValidateCapabilities(MonsterValidationContext context)
	{
		Node Wired(GodotObject owner, string property) => MonsterValidationContext.Object<Node>(owner, property);
		var controller = Wired(context.Actor, "DefenseController");
		if (controller != null) context.CheckWiring(context.Local(controller) && Wired(controller, "Enemy") == context.Actor,
			"DefenseController.Enemy: assign this actor to its local defense controller.");
		context.CheckWiring((GuardChance == 0 && DodgeChance == 0) || controller != null, "EnemyDefenseController: add a controller for nonzero response probabilities.");
		foreach (var (chance, name, definitionProperty, clipProperties) in new[]
		{
			(GuardChance, "GuardComponent", "GuardDef", new[] { "GuardHoldClip", "RecoveryClip", "BlockHitClip", "ParryClip" }),
			(DodgeChance, "DodgeComponent", "DodgeDef", new[] { "DodgeBackwardClip", "DodgeForwardClip", "DodgeLeftClip", "DodgeRightClip", "RecoveryClip" })
		})
		{
			if (chance <= 0) continue;
			var capability = Wired(context.Actor, name);
			context.CheckWiring(context.Local(capability) && Wired(controller, name) == capability,
				$"DefenseController.{name}: assign this actor's {name}; empty means unavailable.");
			var capabilityDefinition = MonsterValidationContext.Object<Resource>(capability, definitionProperty);
			context.CheckWiring(capability != null && capabilityDefinition != null && Wired(capability, "Actor") == context.Actor
				&& Wired(capability, "ActionGate") == context.ActionGate && Wired(capability, "AnimationComponent") == context.AnimationComponent,
				$"{name}: bind actor, action gate, animation and definition.");
			var clips = MonsterValidationContext.Object<Resource>(capabilityDefinition, "AnimationProfile");
			context.CheckWiring(clips != null && System.Linq.Enumerable.All(clipProperties, property => (property == "ParryClip"
				&& capabilityDefinition.Get("CanParry").AsBool() == false) || context.HasClip(clips.Get(property).AsStringName())),
				$"{name}: assign valid defense and recovery clips.");
		}
	}
}
