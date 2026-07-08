using Godot;

/// <summary>Relocates the player to a random free tile anywhere on the current map.</summary>
[GlobalClass]
public partial class TeleportScrollEffect : ScrollEffect
{
	public override void Apply(Player player)
	{
		if (player == null)
		{
			return;
		}

		Level level = player.GetAncestorOrNull<Level>();
		if (level?.MapGenerator == null)
		{
			return;
		}

		if (level.MapGenerator.TryPickRandomFreePosition(out Vector3 position))
		{
			player.GlobalPosition = position;
		}
	}
}
