namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests the player components that turn statuses into gameplay: confusion turning movement input and
/// slowness driving the game clock. The components subscribe in <c>_Ready</c>, so each test adds its
/// component to the tree; physics steps are invoked directly so the test controls time.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class StatusEffectComponentTest
{
	private static Window Root => ((SceneTree)Engine.GetMainLoop()).Root;

	[TestCase]
	public void ConfusionTurnsMovementOnlyWhileConfused()
	{
		var confused = new StatusCondition();
		var input = AutoFree(new InputComponent());
		var buffs = AutoFree(new BuffController());
		var confusion = AutoFree(new ConfusionComponent { Input = input, Buffs = buffs, Condition = confused });
		confusion.ConfigureRandom(11);
		Root.AddChild(confusion);
		AssertBool(confusion.IsPhysicsProcessing()).IsFalse();

		buffs.ApplyBuff(new Buff { Name = "Confusion", Duration = 5f, Condition = confused });
		AssertBool(confusion.IsPhysicsProcessing()).IsTrue();
		input.InputDirection = Vector3.Forward;
		confusion._PhysicsProcess(0.25);
		AssertFloat(input.InputDirection.AngleTo(Vector3.Forward)).IsGreater(0f);
		AssertFloat(input.InputDirection.Length()).IsEqualApprox(1f, 0.001f);

		// Standing still stays still, and the stagger stops as soon as the confusion ends.
		input.InputDirection = Vector3.Zero;
		confusion._PhysicsProcess(0.25);
		AssertVector(input.InputDirection).IsEqual(Vector3.Zero);
		buffs.ClearBuffs();
		AssertBool(confusion.IsPhysicsProcessing()).IsFalse();
		Root.RemoveChild(confusion);
	}

	[TestCase]
	public void SlownessScalesGameTimeToTheSlowestActiveBuffAndRestoresIt()
	{
		var buffs = AutoFree(new BuffController());
		var component = new TimeScaleComponent { Buffs = buffs };
		Root.AddChild(component);
		try
		{
			var mild = new TimeScaleBuff { Name = "Mild", Duration = 5f, TimeScale = 0.75f };
			var strong = new TimeScaleBuff { Name = "Strong", Duration = 5f, TimeScale = 0.45f };
			buffs.ApplyBuff(mild);
			AssertFloat((float)Engine.TimeScale).IsEqualApprox(0.75f, 0.001f);
			buffs.ApplyBuff(strong);
			AssertFloat((float)Engine.TimeScale).IsEqualApprox(0.45f, 0.001f);
			buffs.RemoveBuff(strong);
			AssertFloat((float)Engine.TimeScale).IsEqualApprox(0.75f, 0.001f);

			// Leaving the tree (floor travel) never strands the game in slow motion.
			Root.RemoveChild(component);
			AssertFloat((float)Engine.TimeScale).IsEqual(1f);
		}
		finally
		{
			Engine.TimeScale = 1.0;
			if (component.IsInsideTree())
			{
				Root.RemoveChild(component);
			}

			component.Free();
		}
	}
}