using Godot;

/// <summary>One scene-composed spell that can be exercised by a combat caller.</summary>
public interface ICombatSpell
{
	string DisplayName { get; }
	int LevelCount { get; }
	bool Cast(Node3D caster, in SpellPower power, RandomNumberGenerator random);
	void Cancel();
}