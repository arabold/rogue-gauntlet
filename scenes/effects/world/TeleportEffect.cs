using Godot;

/// <summary>Fade to light, relocate, then fade back. A canceled visual completes committed travel without replaying arrival.</summary>
public static class TeleportEffect
{
	public static void Play(Node3D actor, WorldEffectHost host, Vector3 destination, WorldEffectDefinition departure, WorldEffectDefinition arrival, uint seed)
	{
		void Relocate(bool showArrival)
		{
			if (!GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree() || (host != null && (!GodotObject.IsInstanceValid(host) || host.IsStopped))) return;
			actor.GlobalPosition = destination;
			if (showArrival) host?.PlayBurst(arrival, seed);
		}
		if (host == null) { Relocate(false); return; }
		var result = host.PlayBurstConfigured(departure, seed, instance =>
		{
			instance.Finished += () => Relocate(instance.CompletedNaturally);
			return true;
		});
		if (result != WorldEffectResult.Started) Relocate(true);
	}
}