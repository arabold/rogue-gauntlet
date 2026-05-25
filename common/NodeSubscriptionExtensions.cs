using Godot;
using System;

public static class NodeSubscriptionExtensions
{
	/// <summary>
	/// Connects an event-like signal and disconnects it when the owner leaves the scene tree.
	/// Use this for long-lived emitters, custom C# signals, resources, and captured delegates.
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

		bool isSubscribed = true;
		subscribe(emitter);
		owner.TreeExiting += OnTreeExiting;
		return Cleanup;

		void OnTreeExiting()
		{
			Cleanup();
		}

		void Cleanup()
		{
			if (!isSubscribed)
			{
				return;
			}

			isSubscribed = false;
			if (GodotObject.IsInstanceValid(owner))
			{
				owner.TreeExiting -= OnTreeExiting;
			}

			if (emitter is GodotObject godotObject && !GodotObject.IsInstanceValid(godotObject))
			{
				return;
			}

			unsubscribe(emitter);
		}
	}
}
