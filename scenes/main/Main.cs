using Godot;
using System;

public partial class Main : Node
{
	[Export]
	public PackedScene EnemyScene { get; set; }

	private Player _player;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Get the player reference
		_player = GetNode<Player>("Player");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void OnEnemyTimerTimeout()
	{
		// Choose a random location on the SpawnPath.
		// We store the reference to the SpawnLocation node.
		var enemySpawnLocation = GetNode<PathFollow3D>("SpawnPath/SpawnLocation");
		// And give it a random offset.
		enemySpawnLocation.ProgressRatio = GD.Randf();

		SpawnEnemy(enemySpawnLocation.Position);
	}

	private void SpawnEnemy(Vector3 position)
	{
		GD.Print("Spawning enemy...");
		Enemy enemy = EnemyScene.Instantiate<Enemy>();
		enemy.Initialize(position);
		AddChild(enemy);

		// Use a deferred call to set the behavior once the Enemy is ready
		enemy.CallDeferred(nameof(Enemy.StartChasing), _player);
	}
}
