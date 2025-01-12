using Godot;
using System.Collections.Generic;

[Tool]
public partial class Level : Node
{
	private LevelGenerator _levelGenerator;
	private PlayerSpawner _playerSpawner;
	// private EnemySpawner _enemySpawner;
	// private ItemSpawner _itemSpawner;

	public override void _Ready()
	{
		_levelGenerator = GetNode<LevelGenerator>("LevelGenerator");
		if (!Engine.IsEditorHint())
		{
			_playerSpawner = GetNode<PlayerSpawner>("PlayerSpawner");
			// _enemySpawner = GetNode<EnemySpawner>("EnemySpawner");
			// _itemSpawner = GetNode<ItemSpawner>("ItemSpawner");
		}

		InitializeLevel();
	}

	private void InitializeLevel()
	{
		_levelGenerator.GenerateMap();

		if (!Engine.IsEditorHint())
		{
			// Spawn the player
			Vector3 playerSpawnPoint = _levelGenerator.PlayerSpawnPoint;
			Node3D player = _playerSpawner.SpawnPlayer(playerSpawnPoint);

			// // Spawn items
			// List<ItemSpawnPoint> itemSpawnPoints = _levelGenerator.ItemSpawnPoints;
			// _itemSpawner.SpawnItems(itemSpawnPoints);

			// // Spawn enemies
			// List<EnemySpawnPoint> enemySpawnPoints = _levelGenerator.EnemySpawnPoints;
			// _enemySpawner.SpawnEnemies(enemySpawnPoints);
		}
	}
}
