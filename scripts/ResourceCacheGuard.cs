using System;
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
