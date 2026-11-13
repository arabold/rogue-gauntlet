using Godot;

/// <summary>
/// An innate cast of a shared <see cref="SpellDefinition"/>: the same spell scene and effects players use, prepared by
/// the actor's <see cref="SpellCaster"/> and timed by its <see cref="AttackController"/>. The target selector decides
/// whether it is an offensive cast, a self cast or an ally cast.
/// </summary>
[Tool]
[GlobalClass]
public partial class MonsterSpellAction : MonsterTimedAction
{
	[ExportGroup("Spell")]
	[Export] public SpellDefinition Spell { get; set; }
	[Export(PropertyHint.Range, "1,5,1")] public int Level { get; set; } = 1;
	/// <summary>Spend the spell's mana cost from the actor's pool; without it the cast is limited by cooldown only.</summary>
	[Export] public bool PaysMana { get; set; }
	/// <summary>
	/// How this monster casts the spell: its contact timing, hyper armor and threat. Empty uses the spell's own cast, as
	/// players cast it; a boss's signature cast authors its own, such as an armored slam nothing interrupts.
	/// </summary>
	[Export] public AttackDefinition Cast { get; set; }

	protected override AttackDefinition Definition => Cast ?? Spell?.Cast;
	public override float Reach => Definition?.Range ?? float.PositiveInfinity;
	public override AttackSlotKind? SlotKind => AttackSlotKind.Ranged;
	private float ManaCost => PaysMana && Spell?.IsValidLevel(Level) == true ? Spell.ManaCost(Level) : 0;

	public override bool IsAvailable(EnemyContext context) => base.IsAvailable(context)
		&& (!PaysMana || context.Actor.ManaComponent.CanSpend(ManaCost));

	/// <summary>The spell would change something for this target; a spell's usefulness implies it can be cast.</summary>
	public override bool IsUsefulOn(EnemyContext context, Node3D target)
	{
		var cast = CastContext(context, target, default);
		return context.Actor.SpellCaster.TryResolve(Spell, this, Level, out var spell) && spell.IsUseful(in cast);
	}

	public override ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run)
	{
		run = null;
		var actor = context.Actor;
		var controller = actor.AttackController;
		if (Spell == null) return ActorActionRequestResult.InvalidDefinition;
		if (PaysMana && !actor.ManaComponent.CanSpend(ManaCost)) return ActorActionRequestResult.Unavailable;
		AttackStats power = ResolvePower(context);
		var cast = CastContext(context, target, power);
		if (!actor.SpellCaster.TryPrepare(Spell, this, in cast, out var release)) return ActorActionRequestResult.Unavailable;
		if (!context.TryReserveSlot(ReservedSlot, out var slots)) return ActorActionRequestResult.Busy;
		release = release with { Revalidate = released => CanRelease(context, released) };
		if (!controller.TryPrepareAttack(Definition, Style, Duration, out var prepared)
			|| !controller.StartAttack(prepared with { Spell = release }, power, HostileMask))
		{
			slots?.Release(actor, yield: false);
			return ActorActionRequestResult.InvalidDefinition;
		}
		// Affordability was checked above in this same call, so the spend cannot fail.
		if (PaysMana) actor.ManaComponent.TrySpend(ManaCost);
		if (IsOffensive) context.NotifyAttackAccepted(controller.CurrentExecutionId);
		run = new MonsterTimedRun(this, target, slots, controller, controller.CurrentExecutionId);
		return ActorActionRequestResult.Success;
	}

	private SpellCastContext CastContext(EnemyContext context, Node3D target, AttackStats power) =>
		new(context.Actor, target, EffectiveTarget.Intent, new SpellPower(Level, power));

	/// <summary>At release the target must still be alive, within range, visible and worth the cast.</summary>
	private bool CanRelease(EnemyContext context, SpellCastContext cast)
	{
		if (context.Actor.IsDead || !GodotObject.IsInstanceValid(cast.Target)) return false;
		if (cast.Target is IBuffTarget { Health.IsDead: true }) return false;
		if (!ReferenceEquals(cast.Target, context.Actor)
			&& (!IsInRange(context.Actor.GlobalPosition.HorizontalDistanceTo(cast.Target.GlobalPosition)) || !context.CanSee(cast.Target))) return false;
		return context.Actor.SpellCaster.TryResolve(Spell, this, Level, out var spell) && spell.IsUseful(in cast);
	}

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		if (!context.Check(Spell?.IsValidLevel(Level) == true, "assign a SpellDefinition that supports this Level.")) return;
		context.Check(!PaysMana || Spell.ManaCost(Level) > 0, "PaysMana needs a spell level with a mana cost.");
		context.Check(!PaysMana || context.Definition?.MaxMana >= Spell.ManaCost(Level), "Definition.MaxMana must cover the spell's mana cost.");
		context.Check(Definition != null, "assign a Cast or a spell with its own Cast attack definition.");
	}
}
