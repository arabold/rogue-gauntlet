/// <summary>Observable contact resolution, distinct from whether the attack geometrically connected.</summary>
public enum DamageOutcome { Damaged, Absorbed, Invulnerable, Filtered, Dodged, Parried, Blocked, GuardBroken }
/// <summary>Zone and Unaware describe directional and sneak-attack contacts so the attacker can emphasize them.</summary>
public readonly record struct DamageResolution(DamageOutcome Outcome, float Amount, HitZone Zone = HitZone.Front, bool Unaware = false);

/// <summary>
/// How an unprotected damaging contact affected its defender. A flinch keeps the current action; an
/// interruption cancels it (actors without poise); a stagger is a poise break owned by the action gate.
/// </summary>
public enum HitReaction { Flinch, Interrupted, Staggered }