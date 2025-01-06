using Godot;
using System.Collections.Generic;

public partial class Level : Node
{
	private MapManager _mapManager;
	private PlayerSpawner _playerSpawner;
	private EnemySpawner _enemySpawner;
	private ItemSpawner _itemSpawner;

	public override void _Ready()
	{
		_mapManager = GetNode<MapManager>("MapManager");
		_playerSpawner = GetNode<PlayerSpawner>("PlayerSpawner");
		_enemySpawner = GetNode<EnemySpawner>("EnemySpawner");
		_itemSpawner = GetNode<ItemSpawner>("ItemSpawner");

		InitializeLevel();
	}

	private void InitializeLevel()
	{
		_mapManager.GenerateMap();

		// Spawn the player
		Vector3 playerSpawnPoint = _mapManager.PlayerSpawnPoint;
		Node3D player = _playerSpawner.SpawnPlayer(playerSpawnPoint);

		// Spawn items
		List<ItemSpawnPoint> itemSpawnPoints = _mapManager.ItemSpawnPoints;
		_itemSpawner.SpawnItems(itemSpawnPoints);

		// Spawn enemies
		List<EnemySpawnPoint> enemySpawnPoints = _mapManager.EnemySpawnPoints;
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
