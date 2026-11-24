using System;
using Godot;

/// <summary>
/// Fade to light, relocate, then fade back. A canceled visual completes committed travel without replaying arrival;
/// travel that <c>committed</c> reports called off (a monster's blink cut short by a stagger) does not happen at all.
/// </summary>
public static class TeleportEffect
{
	/// <summary>
	/// Plays <paramref name="departure"/> on <paramref name="host"/> and moves <paramref name="actor"/> to
	/// <paramref name="destination"/> when it ends, then plays <paramref name="arrival"/> and calls
	/// <paramref name="arrived"/>. Returns the departure, so the caller can cut it short; null when the actor moved at once.
	/// </summary>
	public static WorldEffectInstance Play(Node3D actor, WorldEffectHost host, Vector3 destination, WorldEffectDefinition departure,
		WorldEffectDefinition arrival, uint seed, Func<bool> committed = null, Action arrived = null)
	{
		void Relocate(bool showArrival)
		{
			if (!GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree() || (host != null && (!GodotObject.IsInstanceValid(host) || host.IsStopped))
				|| committed?.Invoke() == false) return;
			actor.GlobalPosition = destination;
			if (actor is CharacterBody3D body) body.Velocity = Vector3.Zero;
			actor.ResetPhysicsInterpolation();
			if (showArrival) host?.PlayBurst(arrival, seed);
			arrived?.Invoke();
		}
		if (host == null) { Relocate(false); return null; }
		WorldEffectInstance started = null;
		var result = host.PlayBurstConfigured(departure, seed, instance =>
		{
			started = instance;
			instance.Finished += () => Relocate(instance.CompletedNaturally);
			return true;
		});
		if (result == WorldEffectResult.Started) return started;
		Relocate(true);
		return null;
	}
}
