using Godot;

/// <summary>One scene-composed spell that can be exercised by a combat caller.</summary>
public interface ICombatSpell
{
	string DisplayName { get; }
	int LevelCount { get; }
	/// <summary>What a cast with <paramref name="power"/> does, as short tooltip lines (see <see cref="SpellLevels"/>).</summary>
	System.Collections.Generic.IEnumerable<string> DescribeLevel(in SpellPower power);
	bool CanCast(in SpellCastContext context);
	bool IsUseful(in SpellCastContext context);
	bool Cast(in SpellCastContext context, RandomNumberGenerator random);
	void Cancel();
}
