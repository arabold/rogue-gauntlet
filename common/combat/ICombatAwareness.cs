using Godot;

/// <summary>
/// Lets a hurtbox ask its actor whether a contact is a sneak attack, then report the hit so the actor
/// notices its attacker. Actors without awareness (the player, practice targets) are never unaware.
/// </summary>
public interface ICombatAwareness
{
	bool IsUnaware { get; }

	/// <summary>Called after an unprotected damaging contact so an unaware actor turns on its attacker.</summary>
	void NoticeAttacker(Node attacker);
}