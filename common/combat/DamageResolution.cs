/// <summary>Observable contact resolution, distinct from whether the attack geometrically connected.</summary>
public enum DamageOutcome { Damaged, Evaded, Absorbed, Invulnerable, Filtered }
public readonly record struct DamageResolution(DamageOutcome Outcome, float Amount);