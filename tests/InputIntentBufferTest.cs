namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Fresh presses wait one window for an action that can accept them, then lapse.</summary>
[TestSuite]
public class InputIntentBufferTest
{
	[TestCase]
	public void PressIsPendingForTheWindowAndConsumedOnce()
	{
		var buffer = new InputIntentBuffer(0.2);
		buffer.Push(PlayerIntent.Dodge, 1.0);
		AssertBool(buffer.IsPending(PlayerIntent.Dodge, 1.2)).IsTrue();
		AssertBool(buffer.IsPending(PlayerIntent.Primary, 1.0)).IsFalse();
		AssertBool(buffer.TryConsume(PlayerIntent.Dodge, 1.1)).IsTrue();
		AssertBool(buffer.TryConsume(PlayerIntent.Dodge, 1.1)).IsFalse();
	}

	[TestCase]
	public void PressLapsesAfterTheWindow()
	{
		var buffer = new InputIntentBuffer(0.2);
		buffer.Push(PlayerIntent.Primary, 1.0);
		AssertBool(buffer.TryConsume(PlayerIntent.Primary, 1.21)).IsFalse();
	}

	[TestCase]
	public void NewerPressRestartsTheWindowAndClearDropsEverything()
	{
		var buffer = new InputIntentBuffer(0.2);
		buffer.Push(PlayerIntent.Primary, 1.0);
		buffer.Push(PlayerIntent.Primary, 1.15);
		AssertBool(buffer.IsPending(PlayerIntent.Primary, 1.3)).IsTrue();
		buffer.Push(PlayerIntent.Secondary, 1.3);
		buffer.Clear();
		AssertBool(buffer.IsPending(PlayerIntent.Primary, 1.3)).IsFalse();
		AssertBool(buffer.IsPending(PlayerIntent.Secondary, 1.3)).IsFalse();
	}
}
