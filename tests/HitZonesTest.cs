namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Facing classification; incoming directions point from attacker to defender. Godot vectors need the runtime.</summary>
[TestSuite]
[RequireGodotRuntime]
public class HitZonesTest
{
	[TestCase]
	public void ContactsAreClassifiedByTheirAngleFromTheDefendersFacing()
	{
		var forward = Vector3.Forward;
		// Facing the attacker means the attack travels against the defender's forward.
		AssertObject(HitZones.Classify(forward, Vector3.Back, 60, 45)).IsEqual(HitZone.Front);
		AssertObject(HitZones.Classify(forward, Vector3.Forward, 60, 45)).IsEqual(HitZone.Rear);
		AssertObject(HitZones.Classify(forward, Vector3.Left, 60, 45)).IsEqual(HitZone.Side);
		AssertObject(HitZones.Classify(forward, Vector3.Back.Rotated(Vector3.Up, Mathf.DegToRad(59)), 60, 45)).IsEqual(HitZone.Front);
		AssertObject(HitZones.Classify(forward, Vector3.Back.Rotated(Vector3.Up, Mathf.DegToRad(61)), 60, 45)).IsEqual(HitZone.Side);
		AssertObject(HitZones.Classify(forward, Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(44)), 60, 45)).IsEqual(HitZone.Rear);
		AssertObject(HitZones.Classify(forward, Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(46)), 60, 45)).IsEqual(HitZone.Side);
	}

	[TestCase]
	public void HeightAndDegenerateDirectionsDoNotCreateRearHits()
	{
		AssertObject(HitZones.Classify(Vector3.Forward, new Vector3(0, -5, -1), 60, 45)).IsEqual(HitZone.Rear);
		AssertObject(HitZones.Classify(Vector3.Forward, Vector3.Down, 60, 45)).IsEqual(HitZone.Front);
		AssertObject(HitZones.Classify(Vector3.Zero, Vector3.Forward, 60, 45)).IsEqual(HitZone.Front);
	}
}