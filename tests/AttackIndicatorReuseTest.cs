namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Warnings retain one native mesh while their shape, pose, tint and fade remain correct.</summary>
[TestSuite, RequireGodotRuntime]
public class AttackIndicatorReuseTest
{
	[TestCase]
	public void MovingOrTintingAWarnedFootprintKeepsItsMeshAndChangedInputsUpdateItsShape()
	{
		var profile = new AttackIndicatorProfile();
		var indicator = TestFactories.AddToSceneTree(new AttackIndicatorComponent { Profile = profile });
		Vector2[] footprint = [new(-1, 0), new(1, 0), new(1, -2), new(-1, -2)];
		var frame = new AttackIndicatorFrame(default, AttackIndicatorStage.Aiming, AttackThreatTier.Normal,
			false, Vector3.Zero, Vector3.Forward, 2, footprint);
		indicator.Present(frame);
		var mesh = indicator.Mesh;
		for (int i = 0; i < 100; i++)
			indicator.Present(frame with { Origin = Vector3.Right * i, ThreatTier = AttackThreatTier.Heavy });
		AssertObject(indicator.Mesh).IsSame(mesh);
		AssertFloat(indicator.GlobalPosition.X).IsEqual(99);
		footprint[1] = new Vector2(3, 0);
		indicator.Present(frame);
		AssertObject(indicator.Mesh).IsSame(mesh);
		AssertFloat(mesh.GetAabb().End.X).IsEqual(3);

		frame = frame with { IsProjectile = true, AimHalfAngleDegrees = 30, AimProgress = 0 };
		indicator.Present(frame);
		float shortLength = mesh.GetAabb().Size.Z;
		indicator.Present(frame with { AimProgress = 1 });
		AssertObject(indicator.Mesh).IsSame(mesh);
		AssertFloat(mesh.GetAabb().Size.Z).IsGreater(shortLength);
		indicator.Present(frame with { Stage = AttackIndicatorStage.Released });
		indicator.End(default, false);
		AssertBool(indicator.IsProcessing()).IsTrue();
		indicator._Process(1);
		AssertBool(indicator.Visible || indicator.IsProcessing()).IsFalse();
	}
}