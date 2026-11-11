using Godot;

/// <summary>Adapts a shared effect strategy to the same ICombatSpell contract used by combat spells.</summary>
[Tool]
public partial class EffectSpell : Node, ICombatSpell
{
	[Export] public SpellEffect Effect { get; set; }
	public string DisplayName => Effect?.ResourceName ?? "";
	public int LevelCount => Effect?.LevelCount ?? 0;
	public System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power) => Effect?.DescribeLevel(power) ?? [];
	public bool CanCast(in SpellCastContext context) => Effect?.CanCast(in context) == true;
	public bool IsUseful(in SpellCastContext context) => Effect?.IsUseful(in context) == true;
	public bool Cast(in SpellCastContext context, RandomNumberGenerator random) => Effect?.Cast(in context, random) == true;
	public void Cancel() { }
}
