using Godot;

/// <summary>One immutable observation of a completed damage-resolution path.</summary>
public readonly record struct CombatContactFeedback(DamageContext Context, HurtBoxComponent Target,
	DamageOutcome Outcome, float Damage, DamageEmphasis Emphasis, HitReaction? Reaction,
	bool ReactionSuppressed, bool Lethal, Vector3 Position, Vector3 IncomingDirection,
	CombatFeedbackSettings Settings);

public delegate void CombatContactFeedbackHandler(in CombatContactFeedback feedback);
