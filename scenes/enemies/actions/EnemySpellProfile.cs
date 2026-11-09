using Godot;
using System;

/// <summary>An innate monster activation of the shared SpellDefinition and ICombatSpell execution.</summary>
[GlobalClass]
public partial class EnemySpellProfile : EnemyActionProfile
{
	[ExportGroup("Spell")]
	[Export] public SpellDefinition Spell { get; set; }
	[Export(PropertyHint.Range, "1,5,1")] public int Level { get; set; } = 1;
	[Export] public bool PaysMana { get; set; }
	[Export] public SpellTargetIntent Intent { get; set; } = SpellTargetIntent.Hostile;
	[Export] public StringName Style { get; set; } = "";
	[Export(PropertyHint.Range, "0,10,0.01")] public float Duration { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float HealingHealthThreshold { get; set; } = 1;
	[Export] public float MinDamage { get; set; }
	[Export] public float MaxDamage { get; set; } = 5;
	[Export(PropertyHint.Range, "0,1,0.01")] public float CritChance { get; set; }

	public override bool IsOffensive => Intent == SpellTargetIntent.Hostile;
	protected override float EffectiveMaximumDistance => Mathf.Min(MaximumDistance, Spell?.Cast?.Range ?? MaximumDistance);

	public override bool HasValidConfiguration => base.HasValidConfiguration && Spell?.IsValidLevel(Level) == true
		&& Enum.IsDefined(Intent) && (!string.IsNullOrEmpty(Style) || !string.IsNullOrEmpty(Spell?.Cast?.AttackStyle))
		&& (!PaysMana || Spell.ManaCost(Level) > 0)
		&& float.IsFinite(Duration) && Duration >= 0
		&& float.IsFinite(HealingHealthThreshold) && HealingHealthThreshold is >= 0 and <= 1
		&& float.IsFinite(MinDamage) && MinDamage >= 0 && float.IsFinite(MaxDamage) && MaxDamage >= MinDamage
		&& float.IsFinite(CritChance) && CritChance is >= 0 and <= 1
		&& (Intent == SpellTargetIntent.Hostile ? SlotKind == EnemyActionSlotKind.Ranged : SlotKind == EnemyActionSlotKind.None)
		&& !RiposteEligible;

	public override bool IsEligible(EnemyContext context, float pressureDuration = 0)
	{
		if (context == null || !HasValidConfiguration) return false;
		if (IsOffensive && !base.IsEligible(context, pressureDuration)) return false;
		if (!IsOffensive && ClosePressureDuration > 0 && pressureDuration < ClosePressureDuration) return false;
		Node3D target = SelectTarget(context);
		return target != null && IsWithinRange(context.Actor, target) && IsBelowHealingThreshold(target)
			&& TryPrepare(context, target, out var release, out var castContext)
			&& release.Spell.IsUseful(in castContext);
	}

	private bool IsBelowHealingThreshold(Node3D target)
	{
		if (Intent == SpellTargetIntent.Hostile || Spell.Effect is not (HealSpellEffect or BuffSpellEffect { Buff: HealingBuff })) return true;
		return target is IBuffTarget { Health: { } health } && health.MaxHealth > 0
			&& health.CurrentHealth / health.MaxHealth <= HealingHealthThreshold;
	}

	/// <summary>Prepare a fresh cast from the actor's local spell cache using the selected target intent.</summary>
	public bool TryPrepare(EnemyContext context, Node3D target, out SpellRelease release, out SpellCastContext castContext)
	{
		release = null;
		castContext = default;
		if (context?.Actor is not EnemyBase actor || actor.SpellCaster == null || !GodotObject.IsInstanceValid(target)) return false;
		if (PaysMana && (actor.ManaComponent == null || !actor.ManaComponent.CanSpend(Spell.ManaCost(Level)))) return false;
		AttackStats power = actor.Stats?.ResolveAttack(MinDamage, MaxDamage, CritChance)
			?? new AttackStats(MinDamage, MaxDamage, CritChance);
		castContext = new SpellCastContext(actor, target, Intent, new SpellPower(Level, power));
		return actor.SpellCaster.TryPrepare(Spell, this, in castContext, out release);
	}

	/// <summary>Revalidate the captured target at release so a moved, dead or occluded support recipient does not receive a stale cast.</summary>
	public bool CanRelease(in SpellCastContext context)
	{
		if (context.Caster is not EnemyBase actor || actor.IsDead || !GodotObject.IsInstanceValid(actor)) return false;
		if (Intent == SpellTargetIntent.Self) return ReferenceEquals(context.Target, actor);
		if (!GodotObject.IsInstanceValid(context.Target) || context.Target is not Node3D target
			|| target is IBuffTarget { Health: { IsDead: true } } || !IsWithinRange(actor, target)) return false;
		if (Intent == SpellTargetIntent.Ally && !target.IsInGroup("enemy")) return false;
		return WorldSight.IsClear(actor.GetWorld3D(), actor.GlobalPosition + Vector3.Up, target.GlobalPosition + Vector3.Up);
	}

	public bool TryPrepare(EnemyContext context, out SpellRelease release, out SpellCastContext castContext) =>
		TryPrepare(context, SelectTarget(context), out release, out castContext);

	/// <summary>Choose self or the most injured useful ally in one pass, then reuse that fact for this decision.</summary>
	private Node3D SelectTarget(EnemyContext context)
	{
		if (Intent == SpellTargetIntent.Hostile) return context.Target;
		if (Intent == SpellTargetIntent.Self) return context.Actor;
		return context.FindSupportTarget(GetInstanceId(), () => FindBestAlly(context));
	}

	private Node3D FindBestAlly(EnemyContext context)
	{
		Node3D best = null;
		float bestHealthFraction = float.PositiveInfinity;
		string bestPath = "";
		foreach (Node node in context.SupportCandidates)
		{
			if (node is not Node3D candidate || candidate is not IBuffTarget { Health: { } health }
				|| health.IsDead || !IsWithinRange(context.Actor, candidate)) continue;
			if (!context.CanSeeSupportTarget(candidate)) continue;
			if (!IsUsefulTarget(context, candidate)) continue;
			float fraction = health.MaxHealth > 0 ? health.CurrentHealth / health.MaxHealth : 1;
			if (fraction < bestHealthFraction)
			{
				best = candidate;
				bestHealthFraction = fraction;
				bestPath = candidate.GetPath().ToString();
			}
			else if (Mathf.IsEqualApprox(fraction, bestHealthFraction))
			{
				string path = candidate.GetPath().ToString();
				if (string.CompareOrdinal(path, bestPath) < 0)
				{
					best = candidate;
					bestPath = path;
				}
			}
		}
		if (context.Actor is IBuffTarget self && self.Health is { IsDead: false } selfHealth
			&& IsWithinRange(context.Actor, context.Actor) && IsUsefulTarget(context, context.Actor))
		{
			float fraction = selfHealth.MaxHealth > 0 ? selfHealth.CurrentHealth / selfHealth.MaxHealth : 1;
			if (fraction < bestHealthFraction) best = context.Actor;
		}
		return best;
	}

	private bool IsUsefulTarget(EnemyContext context, Node3D target) => TryPrepare(context, target, out var release, out var castContext)
		&& release.Spell.IsUseful(in castContext);

	private bool IsWithinRange(Node3D actor, Node3D target)
	{
		float distance = actor.GlobalPosition.HorizontalDistanceTo(target.GlobalPosition);
		return distance >= MinimumDistance && distance <= EffectiveMaximumDistance;
	}
}
