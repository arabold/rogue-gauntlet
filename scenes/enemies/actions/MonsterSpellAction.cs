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

	protected override AttackDefinition Definition => Spell?.Cast;
	public override float Reach => Spell?.Cast?.Range ?? float.PositiveInfinity;
	public override AttackSlotKind? SlotKind => AttackSlotKind.Ranged;
	private float ManaCost => PaysMana && Spell?.IsValidLevel(Level) == true ? Spell.ManaCost(Level) : 0;

	public override bool IsAvailable(EnemyContext context) => base.IsAvailable(context) && context.Enemy.SpellCaster != null
		&& (!PaysMana || context.Enemy.ManaComponent?.CanSpend(ManaCost) == true);

	public override bool IsUsefulOn(EnemyContext context, Node3D target)
	{
		var cast = CastContext(context, target, default);
		return context.Enemy?.SpellCaster?.TryResolve(Spell, this, Level, out var spell) == true
			&& spell.CanCast(in cast) && spell.IsUseful(in cast);
	}

	public override ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run)
	{
		run = null;
		var enemy = context.Enemy;
		var controller = enemy?.AttackController;
		if (controller == null || enemy.SpellCaster == null || Spell == null) return ActorActionRequestResult.Unavailable;
		var mana = enemy.ManaComponent;
		if (PaysMana && mana?.CanSpend(ManaCost) != true) return ActorActionRequestResult.Unavailable;
		AttackStats power = ResolvePower(context);
		var cast = CastContext(context, target, power);
		if (!enemy.SpellCaster.TryPrepare(Spell, this, in cast, out var release)) return ActorActionRequestResult.Unavailable;
		if (!context.TryReserveSlot(ReservedSlot, out var slots)) return ActorActionRequestResult.Busy;
		release = release with { Revalidate = released => CanRelease(context, released) };
		if (PaysMana && !mana.TrySpend(ManaCost))
		{
			slots?.Release(context.Actor, yield: false);
			return ActorActionRequestResult.Unavailable;
		}
		if (!controller.TryPrepareAttack(Spell.Cast, Style, Duration, out var prepared)
			|| !controller.StartAttack(prepared with { Spell = release }, power, HostileMask))
		{
			if (PaysMana) mana.SetMana(mana.CurrentMana + ManaCost, mana.MaxMana);
			slots?.Release(context.Actor, yield: false);
			return ActorActionRequestResult.InvalidDefinition;
		}
		if (IsOffensive) context.NotifyAttackAccepted(controller.CurrentExecutionId);
		run = new MonsterTimedRun(this, target, slots, controller, controller.CurrentExecutionId);
		return ActorActionRequestResult.Success;
	}

	private SpellCastContext CastContext(EnemyContext context, Node3D target, AttackStats power) =>
		new(context.Actor, target, EffectiveTarget.Intent, new SpellPower(Level, power));

	/// <summary>At release the target must still be alive, within range, visible and worth the cast.</summary>
	private bool CanRelease(EnemyContext context, SpellCastContext cast)
	{
		if (context.Enemy == null || context.Enemy.IsDead || !GodotObject.IsInstanceValid(cast.Target)) return false;
		if (cast.Target is IBuffTarget { Health.IsDead: true }) return false;
		if (!ReferenceEquals(cast.Target, context.Actor))
		{
			float distance = context.Actor.GlobalPosition.HorizontalDistanceTo(cast.Target.GlobalPosition);
			if (distance < MinimumRange || distance > EffectiveMaximumRange || !context.CanSee(cast.Target)) return false;
		}
		return context.Enemy.SpellCaster.TryResolve(Spell, this, Level, out var spell) && spell.IsUseful(in cast);
	}

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		if (!context.Check(Spell?.IsValidLevel(Level) == true, "assign a SpellDefinition that supports this Level.")) return;
		context.Check(!PaysMana || Spell.ManaCost(Level) > 0, "PaysMana needs a spell level with a mana cost.");
		context.Check(!PaysMana || context.Definition?.MaxMana >= Spell.ManaCost(Level), "Definition.MaxMana must cover the spell's mana cost.");
		context.CheckLocalNode("SpellCaster", "SpellCaster: spell actions need this actor's SpellCaster.");
		context.Check(Spell.Cast != null, "the spell needs a Cast attack definition.");
	}
}
