using Godot;

/// <summary>Adapts existing direct spell fixtures to the context used by production cast sources.</summary>
public static class SpellTestExtensions
{
	public static bool Cast(this ICombatSpell spell, Node3D caster, in SpellPower power, RandomNumberGenerator random)
	{
		var context = new SpellCastContext(caster, null, SpellTargetIntent.Hostile, power);
		return spell.Cast(in context, random);
	}
}
