using Godot;
using System.Collections.Generic;

public partial class Level : Node
{
	private LevelGenerator _levelGenerator;
	private PlayerSpawner _playerSpawner;
	private EnemySpawner _enemySpawner;
	private ItemSpawner _itemSpawner;

	public override void _Ready()
	{
		_levelGenerator = GetNode<LevelGenerator>("LevelGenerator");
		_playerSpawner = GetNode<PlayerSpawner>("PlayerSpawner");
		_enemySpawner = GetNode<EnemySpawner>("EnemySpawner");
		_itemSpawner = GetNode<ItemSpawner>("ItemSpawner");

		InitializeLevel();
	}

	private void InitializeLevel()
	{
		_levelGenerator.GenerateMap();

		// Spawn the player
		Vector3 playerSpawnPoint = _levelGenerator.PlayerSpawnPoint;
		Node3D player = _playerSpawner.SpawnPlayer(playerSpawnPoint);

		// Spawn items
		List<ItemSpawnPoint> itemSpawnPoints = _levelGenerator.ItemSpawnPoints;
		_itemSpawner.SpawnItems(itemSpawnPoints);

		// Spawn enemies
		List<EnemySpawnPoint> enemySpawnPoints = _levelGenerator.EnemySpawnPoints;
		_enemySpawner.SpawnEnemies(enemySpawnPoints);

		// Assign player as the target for all enemies
		foreach (Node enemy in _enemySpawner.GetChildren())
		{
			if (enemy is Enemy enemyScript)
			{
				enemyScript.StartChasing(player);
			}
		}
	}
}
