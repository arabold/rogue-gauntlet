/// <summary>Observable contact resolution, distinct from whether the attack geometrically connected.</summary>
public enum DamageOutcome { Damaged, Absorbed, Invulnerable, Filtered, Dodged, Parried, Blocked }
public readonly record struct DamageResolution(DamageOutcome Outcome, float Amount);