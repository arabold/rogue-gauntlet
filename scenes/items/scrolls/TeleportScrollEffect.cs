using Godot;

/// <summary>Relocates the player to a random free tile anywhere on the current map.</summary>
[Tool]
[GlobalClass]
public partial class TeleportScrollEffect : ScrollEffect
{
	[Export] public WorldEffectDefinition Departure { get; set; }
	[Export] public WorldEffectDefinition Arrival { get; set; }

	public override void Apply(Player player)
	{
		if (player == null)
		{
			return;
		}

		GeneratedFloor floor = player.GetAncestorOrNull<Level>()?.Floor;
		if (floor != null && floor.TryPickRandomFreePosition(out Vector3 position))
		{
			TeleportEffect.Play(player, player.WorldEffects, position, Departure, Arrival, 271);
		}
	}
}