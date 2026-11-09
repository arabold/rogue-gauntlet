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

	public override bool IsUseful(in SpellCastContext context) =>
		CanCast(in context) && Buff.IsUsefulOn((IBuffTarget)Recipient(in context));

	public override bool Cast(in SpellCastContext context, RandomNumberGenerator random)
	{
		if (random == null || !CanCast(in context)) return false;
		return ((IBuffTarget)Recipient(in context)).Buffs.ApplyBuff(Buff) is BuffApplication.Applied or BuffApplication.Refreshed;
	}

	public override System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power) =>
		Buff == null ? [] : [$"{Buff.Name} for {Buff.Duration:0.#} seconds"];
}
