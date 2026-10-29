/// <summary>Observable contact resolution, distinct from whether the attack geometrically connected.</summary>
public enum DamageOutcome { Damaged, Absorbed, Invulnerable, Filtered, Dodged, Parried, Blocked, GuardBroken }
/// <summary><paramref name="HitStopScale"/> lets the defender's vulnerability emphasize rear and sneak hits in the attacker's hit-stop.</summary>
public readonly record struct DamageResolution(DamageOutcome Outcome, float Amount, float HitStopScale = 1);

/// <summary>What made a damaging contact land harder, for presentation such as floating damage numbers.</summary>
[System.Flags]
public enum DamageEmphasis { None = 0, Critical = 1, Rear = 2, Sneak = 4 }

/// <summary>
/// How an unprotected damaging contact affected its defender. A flinch keeps the current action; an
/// interruption cancels it (actors without poise); a stagger is a poise break owned by the action gate.
/// </summary>
public enum HitReaction { Flinch, Interrupted, Staggered }