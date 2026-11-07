namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Resolves a dash heading from current input, brief recent input, then facing.</summary>
[TestSuite]
[RequireGodotRuntime]
public class DodgeDirectionMemoryTest
{
	[TestCase]
	public void CurrentDirectionWinsAndNormalizesOnTheHorizontalPlane()
	{
		var memory = new DodgeDirectionMemory();
		memory.Observe(Vector3.Left, 1.0);

		AssertThat(memory.Resolve(new Vector3(2, 0, 0), Vector3.Forward, 1.01, 0.08)).IsEqual(Vector3.Right);
		AssertThat(memory.Resolve(new Vector3(0, 4, 0), Vector3.Forward, 1.01, 0.08)).IsEqual(Vector3.Left);
	}

	[TestCase]
	public void RecentDirectionSurvivesReleaseUntilItsStrictExpiryBoundary()
	{
		var memory = new DodgeDirectionMemory();
		memory.Observe(Vector3.Right, 0);
		memory.Observe(Vector3.Zero, 0.02);
		memory.Observe(new Vector3(float.NaN, 0, 1), 0.03);

		AssertThat(memory.Resolve(Vector3.Zero, Vector3.Forward, 0.079, 0.08)).IsEqual(Vector3.Right);
		AssertThat(memory.Resolve(Vector3.Zero, Vector3.Forward, 0.08, 0.08)).IsEqual(Vector3.Forward);
		AssertThat(memory.Resolve(Vector3.Zero, Vector3.Back, 0.081, 0.08)).IsEqual(Vector3.Back);
	}

	[TestCase]
	public void ZeroDurationClearAndInvalidFacingFallBackSafely()
	{
		var memory = new DodgeDirectionMemory();
		memory.Observe(Vector3.Left, 1.0);

		AssertThat(memory.Resolve(Vector3.Zero, Vector3.Forward, 1.01, 0)).IsEqual(Vector3.Forward);
		AssertThat(memory.Resolve(Vector3.Right, Vector3.Forward, 1.01, 0)).IsEqual(Vector3.Right);
		AssertThat(memory.Resolve(Vector3.Zero, Vector3.Up, 1.01, double.NaN)).IsEqual(Vector3.Forward);

		memory.Clear();
		memory.Clear();
		AssertThat(memory.Resolve(Vector3.Zero, Vector3.Left, 1.01, 0.08)).IsEqual(Vector3.Left);
	}
}