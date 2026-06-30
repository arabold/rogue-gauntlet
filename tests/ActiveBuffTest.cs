namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="ActiveBuff"/> duration and periodic-tick behavior, including the
/// regression where a large frame near expiry must not drive extra ticks past the buff's
/// end. Needs the Godot runtime: <see cref="ActiveBuff"/> is a Node and <see cref="Buff"/>
/// is a Resource. <see cref="ActiveBuff._PhysicsProcess"/> is called directly so the test
/// controls the timestep without relying on the engine clock.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ActiveBuffTest
{
	/// <summary>A periodic buff that counts its lifecycle callbacks; ignores the (null) player.</summary>
	private sealed partial class CountingPeriodicBuff : PeriodicBuff
	{
		public int ApplyCount;
		public int RemoveCount;
		public int TickCount;

		public override void OnApply(Player player) => ApplyCount++;
		public override void OnRemove(Player player) => RemoveCount++;
		public override void OnTick(Player player) => TickCount++;
	}

	[TestCase]
	public void InitializeAppliesTheBuffOnce()
	{
		var buff = new CountingPeriodicBuff { Duration = 5f, TicksPerSecond = 2f };
		var active = AutoFree(new ActiveBuff());
		active.Initialize(null, buff);

		AssertInt(buff.ApplyCount).IsEqual(1);
		AssertFloat(active.RemainingDuration).IsEqual(5.0);
		AssertBool(active.IsPermanent).IsFalse();
		AssertBool(active.IsExpired).IsFalse();
	}

	[TestCase]
	public void PeriodicBuffTicksAtItsConfiguredRate()
	{
		// 2 ticks/sec => one tick per 0.5s (exact in floating point).
		var buff = new CountingPeriodicBuff { Duration = 10f, TicksPerSecond = 2f };
		var active = AutoFree(new ActiveBuff());
		active.Initialize(null, buff);

		active._PhysicsProcess(1.0);
		AssertInt(buff.TickCount).IsEqual(2);

		active._PhysicsProcess(0.25); // accumulates, below the 0.5 interval
		AssertInt(buff.TickCount).IsEqual(2);

		active._PhysicsProcess(0.25); // now 0.5 accumulated => one more tick
		AssertInt(buff.TickCount).IsEqual(3);
	}

	[TestCase]
	public void LargeFrameNearExpiryDoesNotOverTick()
	{
		// Regression: a huge delta must be clamped to the remaining duration so ticks cannot
		// run far past the buff's end. Duration 1s at 10 ticks/sec caps ticks at ~10, not
		// the ~1000 an unclamped 100s frame would produce.
		var buff = new CountingPeriodicBuff { Duration = 1f, TicksPerSecond = 10f };
		var active = AutoFree(new ActiveBuff());
		active.Initialize(null, buff);

		active._PhysicsProcess(100.0);

		AssertInt(buff.TickCount).IsGreaterEqual(1);
		AssertInt(buff.TickCount).IsLessEqual(10);
		AssertBool(active.IsExpired).IsTrue();
		AssertInt(buff.RemoveCount).IsEqual(1);
	}

	[TestCase]
	public void ExpiryRemovesTheBuffOnceAndStops()
	{
		var buff = new CountingPeriodicBuff { Duration = 1f, TicksPerSecond = 10f };
		var active = AutoFree(new ActiveBuff());
		active.Initialize(null, buff);

		active._PhysicsProcess(2.0); // expires this frame
		int ticksAtExpiry = buff.TickCount;
		AssertInt(buff.RemoveCount).IsEqual(1);

		// Further processing after removal is a no-op: no extra ticks, no second removal.
		active._PhysicsProcess(2.0);
		AssertInt(buff.TickCount).IsEqual(ticksAtExpiry);
		AssertInt(buff.RemoveCount).IsEqual(1);
	}

	[TestCase]
	public void DeactivateIsIdempotent()
	{
		var buff = new CountingPeriodicBuff { Duration = 5f, TicksPerSecond = 1f };
		var active = AutoFree(new ActiveBuff());
		active.Initialize(null, buff);

		active.Deactivate();
		active.Deactivate();

		AssertInt(buff.RemoveCount).IsEqual(1);
	}

	[TestCase]
	public void PermanentBuffNeverExpires()
	{
		// Duration 0 means permanent: it accrues full frames and is never auto-removed.
		var buff = new CountingPeriodicBuff { Duration = 0f, TicksPerSecond = 2f };
		var active = AutoFree(new ActiveBuff());
		active.Initialize(null, buff);

		AssertBool(active.IsPermanent).IsTrue();

		active._PhysicsProcess(1000.0);

		AssertBool(active.IsExpired).IsFalse();
		AssertInt(buff.RemoveCount).IsEqual(0);
		AssertInt(buff.TickCount).IsGreater(0);
	}
}