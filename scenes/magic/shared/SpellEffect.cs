using Godot;

/// <summary>A stateless, reusable spell outcome hosted by the common combat spell executor.</summary>
[Tool]
[GlobalClass]
public abstract partial class SpellEffect : Resource
{
	public abstract int LevelCount { get; }
	public abstract bool CanCast(in SpellCastContext context);
	public virtual bool IsUseful(in SpellCastContext context) => CanCast(in context);
	public abstract bool Cast(in SpellCastContext context, RandomNumberGenerator random);
	public abstract System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power);

	protected static Node3D Recipient(in SpellCastContext context) => context.Intent == SpellTargetIntent.Self
		? context.Caster : context.Target;
}
