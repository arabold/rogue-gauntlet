namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Scene teardown releases pool membership; cached reuse retains it only while nodes are alive.</summary>
[TestSuite, RequireGodotRuntime]
public class ScenePoolTest
{
	[TestCase]
	public void CachedProjectilesReleaseTheirPreviousTargets()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/attacks/projectile.tscn");
		var world = TestFactories.AddToSceneTree(new Node3D());
		var projectile = ScenePool.Spawn<Projectile>(scene, world);
		var target = AssignFreedTarget(projectile);
		ResourceCacheGuard.Settle();
		AssertBool(target.IsAlive).IsTrue();
		ScenePool.Despawn(projectile);
		ResourceCacheGuard.Settle();
		AssertBool(ScenePool.IsTracked(projectile)).IsTrue();
		AssertBool(target.IsAlive).IsFalse();
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static System.WeakReference AssignFreedTarget(Projectile projectile)
	{
		var target = new Node3D();
		projectile.RestrictToTarget(target);
		target.Free();
		return new System.WeakReference(target);
	}

	[TestCase]
	public void DestroyingWorldsReleasesTheirActivePoolMembers()
	{
		var scene = new PackedScene();
		using var prototype = new Node3D();
		scene.Pack(prototype);
		string baseline = ScenePool.GetDebugSummary();
		for (int i = 0; i < 16; i++)
		{
			var world = new Node3D();
			((SceneTree)Engine.GetMainLoop()).Root.AddChild(world);
			var node = ScenePool.Spawn<Node3D>(scene, world);
			AssertBool(ScenePool.IsTracked(node)).IsTrue();
			world.Free();
			AssertBool(ScenePool.IsTracked(node)).IsFalse();
			AssertString(ScenePool.GetDebugSummary()).IsEqual(baseline);
		}
	}

	[TestCase]
	public void NormalReuseAndExternalCachedDestructionKeepThePoolUsable()
	{
		var scene = new PackedScene();
		using var prototype = new Node3D();
		scene.Pack(prototype);
		var world = TestFactories.AddToSceneTree(new Node3D());
		var node = ScenePool.Spawn<Node3D>(scene, world);
		ScenePool.Despawn(node);
		AssertBool(ScenePool.IsTracked(node)).IsTrue();
		var reused = ScenePool.Spawn<Node3D>(scene, world);
		AssertObject(reused).IsSame(node);
		ScenePool.Despawn(reused);
		reused.Free();
		AssertBool(ScenePool.IsTracked(reused)).IsFalse();
		var replacement = ScenePool.Spawn<Node3D>(scene, world);
		AssertBool(GodotObject.IsInstanceValid(replacement)).IsTrue();
		AssertObject(replacement).IsNotSame(reused);
		AssertBool(ScenePool.IsTracked(replacement)).IsTrue();
	}
}