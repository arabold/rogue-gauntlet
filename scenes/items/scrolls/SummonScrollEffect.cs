using Godot;

/// <summary>
/// A risk/reward hazard: summons hostile enemies near the player. Spawned enemies are
/// ephemeral (no <see cref="SpawnPoint.PersistentId"/>), so unlike authored map spawns
/// they do not survive a save/reload — acceptable for a one-shot curse effect.
/// </summary>
[Tool]
[GlobalClass]
public partial class SummonScrollEffect : ScrollEffect
{
	[Export] public int Count { get; set; } = 3;
	[Export] public float Radius { get; set; } = 4f;

	public override void Apply(Player player)
	{
		if (player == null || Count <= 0)
		{
			return;
		}

		Level level = player.GetAncestorOrNull<Level>();
		MapGenerator generator = level?.MapGenerator;
		if (generator?.MobFactory == null)
		{
			return;
		}

		using var rng = GameSession.Instance?.CreateLootRng()
			?? GenerationRandom.Create(generator.Seed, "summon");
		Vector3 playerPosition = player.GlobalPosition;
		for (int i = 0; i < Count; i++)
		{
			PackedScene enemyScene = generator.MobFactory.CreateEnemy(generator.DungeonDepth, rng);
			if (enemyScene == null)
			{
				continue;
			}

			float angle = Mathf.Tau * i / Count;
			var ringOffset = new Vector3(Mathf.Cos(angle) * Radius, 0, Mathf.Sin(angle) * Radius);
			Vector3 spawnPosition = generator.FindFreeSpawnPositionNear(playerPosition + ringOffset, Radius);

			var enemy = enemyScene.Instantiate<Node3D>();
			level.AddWorldNode(enemy, spawnPosition);
		}
	}
}