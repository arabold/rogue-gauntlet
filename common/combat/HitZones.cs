using Godot;

/// <summary>Which side of the defender a contact arrived from.</summary>
public enum HitZone { Front, Side, Rear }

/// <summary>Engine-independent facing classification for directional damage.</summary>
public static class HitZones
{
	/// <summary>
	/// Classify a contact by the angle between the defender's facing and where the attack came from.
	/// <paramref name="incomingDirection"/> points from attacker to defender (the attack's travel), so a
	/// rear hit travels along the defender's forward. Degenerate directions count as frontal.
	/// </summary>
	public static HitZone Classify(Vector3 defenderForward, Vector3 incomingDirection, float frontHalfAngle, float rearHalfAngle)
	{
		float fromFront = defenderForward.HorizontalAngleTo(-incomingDirection);
		if (fromFront <= frontHalfAngle) return HitZone.Front;
		return 180f - fromFront <= rearHalfAngle ? HitZone.Rear : HitZone.Side;
	}
}