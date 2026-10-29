namespace RogueGauntlet.Tests;

using System;
using System.Threading.Tasks;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="NodeSubscriptionExtensions.SubscribeUntilExit"/> holds a subscription only while its owner is in the
/// tree, and restores it when the owner comes back, as pooled and reparented nodes do.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NodeSubscriptionTest
{
	private sealed class Emitter
	{
		public event Action Fired;
		public void Fire() => Fired?.Invoke();
	}

	private static Action Watch(Node owner, Emitter emitter, Action onFired) =>
		owner.SubscribeUntilExit(emitter, e => e.Fired += onFired, e => e.Fired -= onFired);

	[TestCase]
	public void LeavingTheTreePausesAndReenteringResumesOnce()
	{
		var emitter = new Emitter();
		int calls = 0;
		var owner = TestFactories.AddToSceneTree(new Node());
		Watch(owner, emitter, () => calls++);
		Node parent = owner.GetParent();

		emitter.Fire();
		parent.RemoveChild(owner);
		emitter.Fire();
		parent.AddChild(owner);
		emitter.Fire();

		AssertInt(calls).IsEqual(2);
	}

	[TestCase]
	public void SubscribingBeforeEnteringTheTreeConnectsOnce()
	{
		var emitter = new Emitter();
		int calls = 0;
		var owner = AutoFree(new Node());
		Watch(owner, emitter, () => calls++);
		((SceneTree)Engine.GetMainLoop()).Root.AddChild(owner);

		emitter.Fire();

		AssertInt(calls).IsEqual(1);
	}

	[TestCase]
	public void EndingTheSubscriptionKeepsItOffAfterReentry()
	{
		var emitter = new Emitter();
		int calls = 0;
		var owner = TestFactories.AddToSceneTree(new Node());
		Action end = Watch(owner, emitter, () => calls++);
		Node parent = owner.GetParent();

		end();
		parent.RemoveChild(owner);
		parent.AddChild(owner);
		emitter.Fire();

		AssertInt(calls).IsEqual(0);
	}

	[TestCase]
	public void AFreedEmitterIsDroppedOnReentry()
	{
		var emitter = new Node();
		int calls = 0;
		void OnRenamed() => calls++;
		var owner = TestFactories.AddToSceneTree(new Node());
		owner.SubscribeUntilExit(emitter, e => e.Renamed += OnRenamed, e => e.Renamed -= OnRenamed);
		Node parent = owner.GetParent();

		parent.RemoveChild(owner);
		emitter.Free();
		parent.AddChild(owner);

		AssertBool(owner.IsInsideTree()).IsTrue();
		AssertInt(calls).IsEqual(0);
	}

	[TestCase]
	public async Task APooledNodeKeepsItsSubscriptionAcrossReuse()
	{
		var scene = new PackedScene();
		scene.Pack(new Node3D());
		Node root = ((SceneTree)Engine.GetMainLoop()).Root;
		var emitter = new Emitter();
		int calls = 0;
		var node = ScenePool.Spawn<Node3D>(scene, root);
		Watch(node, emitter, () => calls++);

		ScenePool.Despawn(node);
		await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
		var reused = ScenePool.Spawn<Node3D>(scene, root);
		emitter.Fire();

		AssertObject(reused).IsSame(node);
		AssertInt(calls).IsEqual(1);
		reused.QueueFree();
	}
}
