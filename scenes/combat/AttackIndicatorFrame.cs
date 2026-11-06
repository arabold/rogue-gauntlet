using Godot;

/// <summary>Authoritative, already-resolved presentation data for one attack warning frame.</summary>
public readonly record struct AttackIndicatorFrame(AttackIdentity Execution, AttackIndicatorStage Stage,
	AttackThreatTier ThreatTier, bool IsProjectile, Vector3 Origin, Vector3 Direction, float Length,
	Vector2[] Footprint, Vector3[] Directions = null, float AimProgress = 1, float AimHalfAngleDegrees = 0);

public enum AttackIndicatorStage { Aiming, Committed, Attacking, Released }
