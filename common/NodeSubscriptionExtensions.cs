using Godot;
using System;

public static class NodeSubscriptionExtensions
{
	/// <summary>
	/// Connects an event-like signal while the owner is in the scene tree: leaving the tree disconnects it and
	/// re-entering connects it again, so pooled and reparented nodes keep their subscriptions (<see cref="ScenePool"/>
	/// moves nodes out of and back into the tree without running <c>_Ready</c> again). Use this for long-lived emitters,
	/// custom C# signals, resources, and captured delegates. The returned action ends the subscription for good.
	/// </summary>
	public static Action SubscribeUntilExit<TEmitter>(
		this Node owner,
		TEmitter emitter,
		Action<TEmitter> subscribe,
		Action<TEmitter> unsubscribe)
		where TEmitter : class
	{
		if (emitter == null)
		{
			return () => { };
		}

		bool isSubscribed = false;
		bool isEnded = false;
		Subscribe();
		owner.TreeExiting += Unsubscribe;
		owner.TreeEntered += OnTreeEntered;
		return End;

		// Subscribing from _EnterTree runs before TreeEntered fires, so connect at most once.
		void Subscribe()
		{
			if (isSubscribed)
			{
				return;
			}

			isSubscribed = true;
			subscribe(emitter);
		}

		void Unsubscribe()
		{
			if (!isSubscribed)
			{
				return;
			}

			isSubscribed = false;
			if (IsFreed())
			{
				return;
			}

			unsubscribe(emitter);
		}

		void OnTreeEntered()
		{
			if (IsFreed())
			{
				End();
				return;
			}

			Subscribe();
		}

		void End()
		{
			if (isEnded)
			{
				return;
			}

			isEnded = true;
			Unsubscribe();
			if (GodotObject.IsInstanceValid(owner))
			{
				owner.TreeExiting -= Unsubscribe;
				owner.TreeEntered -= OnTreeEntered;
			}
		}

		bool IsFreed() => emitter is GodotObject godotObject && !GodotObject.IsInstanceValid(godotObject);
	}
}
