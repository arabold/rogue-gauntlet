using Godot;

/// <summary>One scene-composed spell that can be exercised by a combat caller.</summary>
public interface ICombatSpell
{
	string DisplayName { get; }
	int LevelCount { get; }
	/// <summary>What a cast with <paramref name="power"/> does, as short tooltip lines (see <see cref="SpellLevels"/>).</summary>
	System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power);
	/// <summary>Whether the cast is legal against the context's caster and target; <see cref="Cast"/> checks it itself.</summary>
	bool CanCast(in SpellCastContext context);
	/// <summary>
	/// Whether the cast is legal and would change anything (a heal on a hurt target). AI sources check it; players may
	/// still cast a useless spell.
	/// </summary>
	bool IsUseful(in SpellCastContext context) => CanCast(in context);
	/// <summary>Cast once; false, with no effect, when <see cref="CanCast"/> fails.</summary>
	bool Cast(in SpellCastContext context, RandomNumberGenerator random);
	void Cancel();
}
