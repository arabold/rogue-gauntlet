using Godot;

/// <summary>Restores flat health through the same clamped HealthComponent used by every actor.</summary>
[Tool]
[GlobalClass]
public partial class HealSpellEffect : SpellEffect
{
	[Export] public float[] MaxHealthFractionByLevel { get; set; } = [0.25f, 0.3f, 0.35f, 0.4f, 0.45f];
	public override int LevelCount => System.Math.Min(SpellLevels.Max, MaxHealthFractionByLevel?.Length ?? 0);

	public override bool CanCast(in SpellCastContext context)
	{
		int level = context.Power.Level - 1;
		Node3D recipient = Recipient(in context);
		return GodotObject.IsInstanceValid(recipient) && recipient is IBuffTarget { Health: not null } target
			&& !target.Health.IsDead && level >= 0 && level < LevelCount
			&& float.IsFinite(MaxHealthFractionByLevel[level]) && MaxHealthFractionByLevel[level] is > 0 and <= 1;
	}

	public override bool IsUseful(in SpellCastContext context) => CanCast(in context)
		&& ((IBuffTarget)Recipient(in context)).Health.CurrentHealth < ((IBuffTarget)Recipient(in context)).Health.MaxHealth;

	public override bool Cast(in SpellCastContext context, RandomNumberGenerator random)
	{
		if (random == null || !CanCast(in context)) return false;
		var health = ((IBuffTarget)Recipient(in context)).Health;
		health.Heal(health.MaxHealth * MaxHealthFractionByLevel[context.Power.Level - 1]);
		return true;
	}

	public override System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power) =>
		power.Level > 0 && power.Level <= LevelCount
			? [$"Heals {MaxHealthFractionByLevel[power.Level - 1] * 100:0}% of maximum health"] : [];
}
