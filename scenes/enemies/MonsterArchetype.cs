/// <summary>
/// The combat role a monster is balanced as. Each role names an archetype in the balance model
/// (<c>scripts/balance/params.py</c>, <c>ARCHETYPES</c>), which sets the monster's health, armor, damage, attack rhythm
/// and speed relative to the standard monster at its <see cref="MonsterDefinition.ThreatLevel"/>.
/// <c>simulate.py derive</c> writes those stats. The game reads the role only to pay a gatekeeper its XP bonus
/// (<see cref="ThreatProfile.Xp"/>). Member names must match the model's archetype names.
/// </summary>
public enum MonsterArchetype
{
	/// <summary>Not balanced yet: the balance tools report it and leave its stats alone.</summary>
	None,
	/// <summary>Weak melee filler that comes in numbers.</summary>
	Fodder,
	/// <summary>Fast, frail melee that swarms and applies a status.</summary>
	Swarmer,
	/// <summary>The yardstick every other archetype is measured against.</summary>
	Standard,
	/// <summary>Slow, armored, hard-hitting melee with a guard.</summary>
	Brute,
	/// <summary>Ranged attacker that keeps its distance.</summary>
	Shooter,
	/// <summary>Homing missiles and support spells that heal its allies.</summary>
	Caster,
	/// <summary>A realm's gatekeeper: health phases, area attacks and a guard.</summary>
	Boss,
}
