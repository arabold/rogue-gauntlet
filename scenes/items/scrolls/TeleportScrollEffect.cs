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

		Level level = player.GetAncestorOrNull<Level>();
		GeneratedFloor floor = level?.Floor;
		if (floor == null)
		{
			return;
		}

		using var rng = level.Run.CreateLootRng(level.Seed);
		if (floor.TryPickRandomFreePosition(rng, out Vector3 position))
		{
			TeleportEffect.Play(player, player.WorldEffects, position, Departure, Arrival, 271);
		}
	}
}