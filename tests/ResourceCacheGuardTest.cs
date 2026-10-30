namespace RogueGauntlet.Tests;

using System.Runtime.CompilerServices;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// The workaround for godotengine/godot#83762: settling must free a cached C# resource whose only owner was a
/// collected wrapper, so no later load can revive it from the cache.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ResourceCacheGuardTest
{
	private const string Path = "user://resource_cache_guard_test.tres";

	// Not inlined, so the wrapper is unreachable once this returns. A raw load on purpose: it creates the hazard under test.
	[MethodImpl(MethodImplOptions.NoInlining)]
#pragma warning disable RS0030
	private static void LoadAndDrop() => AssertObject(GD.Load<DodgeDefinition>(Path)).IsNotNull();
#pragma warning restore RS0030

	[TestCase]
	public void SettlingEvictsResourcesOwnedOnlyByCollectedWrappers()
	{
		AssertInt((int)ResourceSaver.Save(new DodgeDefinition(), Path)).IsEqual((int)Error.Ok);
		LoadAndDrop();
		AssertBool(ResourceLoader.HasCached(Path)).IsTrue();

		ResourceCacheGuard.Settle();

		AssertBool(ResourceLoader.HasCached(Path)).IsFalse();
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
	}
}
