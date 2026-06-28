namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Pure-logic tests for the <see cref="Cooldown"/> struct. No Godot runtime needed —
/// this also doubles as the smoke test proving the gdUnit4 harness discovers and runs.
/// </summary>
[TestSuite]
public class CooldownTest
{
	[TestCase]
	public void NewCooldownIsReady()
	{
		var cooldown = new Cooldown();
		AssertBool(cooldown.IsReady).IsTrue();
	}

	[TestCase]
	public void StartedCooldownIsNotReady()
	{
		var cooldown = new Cooldown();
		cooldown.Start(1.0f);
		AssertBool(cooldown.IsReady).IsFalse();
	}

	[TestCase]
	public void TickReportsNotReadyUntilDurationElapses()
	{
		var cooldown = new Cooldown();
		cooldown.Start(1.0f);

		AssertBool(cooldown.Tick(0.4)).IsFalse();
		AssertBool(cooldown.IsReady).IsFalse();

		AssertBool(cooldown.Tick(0.6)).IsTrue();
		AssertBool(cooldown.IsReady).IsTrue();
	}

	[TestCase]
	public void OvershootingTickIsReady()
	{
		var cooldown = new Cooldown();
		cooldown.Start(0.5f);
		AssertBool(cooldown.Tick(10.0)).IsTrue();
	}

	[TestCase]
	public void RestartResetsElapsedState()
	{
		var cooldown = new Cooldown();
		cooldown.Start(1.0f);
		cooldown.Tick(2.0);
		AssertBool(cooldown.IsReady).IsTrue();

		cooldown.Start(1.0f);
		AssertBool(cooldown.IsReady).IsFalse();
	}
}