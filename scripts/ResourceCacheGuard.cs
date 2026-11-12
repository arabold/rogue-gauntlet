using System;
using System.Runtime;
using Godot;

/// <summary>
/// Works around a Godot .NET race (godotengine/godot#83762). When a cached resource's C# wrapper is its last
/// owner, the GC can collect the wrapper before its finalizer releases the native resource. A load that depends
/// on that resource inside this window revives it from the <c>ResourceCache</c> without a managed instance: C#
/// reads it as null (an exported resource comes back empty) and debug builds abort once the finalizer runs.
/// Settling runs those finalizers so dead resources leave the cache before a load can revive them.
/// </summary>
public static class ResourceCacheGuard
{
	private const int MaxPasses = 8;
	/// <summary>Allocation the startup scene's load may make without a collection; a larger load collects as usual.</summary>
	private const long StartupBudget = 192L * 1024 * 1024;
	private static bool _holdingStartup;

	/// <summary>
	/// Suspends collections while the engine loads the startup scene. No load boundary precedes that load, so
	/// <see cref="Settle"/> cannot protect it, and a collection in its middle can revive a dependency it already loaded.
	/// Called before the startup scene loads; <see cref="EndStartup"/> resumes collections.
	/// </summary>
	public static void BeginStartup()
	{
		try { _holdingStartup = GC.TryStartNoGCRegion(StartupBudget); }
		catch (Exception error) when (error is ArgumentOutOfRangeException or InvalidOperationException)
		{
			GD.PushWarning($"Startup loads run with collections: {error.Message}");
		}
	}

	/// <summary>Resumes collections after the startup scene loaded and settles what the load left behind.</summary>
	public static void EndStartup()
	{
		if (!_holdingStartup) return;
		_holdingStartup = false;
		// A load beyond the budget already ended the region with a collection.
		if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
		Settle();
	}

	/// <summary>
	/// Collects unreachable wrappers and waits for their finalizers. Blocking, so call it at load boundaries
	/// such as floor generation, never per frame.
	/// </summary>
	public static void Settle()
	{
		// Freeing one resource can leave another owned only by its unreachable wrapper, so repeat until a pass
		// frees no cached resource.
		double cached = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
		for (int pass = 0; pass < MaxPasses; pass++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			double remaining = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
			if (remaining >= cached) return;
			cached = remaining;
		}
	}
}
