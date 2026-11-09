using Godot;

/// <summary>Applies the same actor-neutral timed buff used by consumables and status sources.</summary>
[Tool]
[GlobalClass]
public partial class BuffSpellEffect : SpellEffect
{
	[Export] public Buff Buff { get; set; }
	public override int LevelCount => SpellLevels.Max;

	public override bool CanCast(in SpellCastContext context)
	{
		Node3D recipient = Recipient(in context);
		return GodotObject.IsInstanceValid(recipient) && recipient is IBuffTarget target
			&& target.Buffs != null && Buff?.CanApplyTo(target) == true;
	}

	public override bool IsUseful(in SpellCastContext context)
	{
		if (!CanCast(in context)) return false;
		Node3D recipient = Recipient(in context);
		var target = (IBuffTarget)recipient;
		if (target.Buffs.HasEquivalent(Buff)) return false;
		return Buff is not HealingBuff healing || target.Health.CurrentHealth < target.Health.MaxHealth
			|| healing.HealthPointsPerTick <= 0;
	}

	public override bool Cast(in SpellCastContext context, RandomNumberGenerator random)
	{
		if (random == null || !CanCast(in context)) return false;
		var controller = ((IBuffTarget)Recipient(in context)).Buffs;
		string key = Buff.Stacking == BuffStacking.Refresh ? Buff.ResourcePath : "";
		return controller.ApplyBuff(Buff, key) is BuffApplication.Applied or BuffApplication.Refreshed;
	}

	public override System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power) =>
		Buff == null ? [] : [$"{Buff.Name} for {Buff.Duration:0.#} seconds"];
}
