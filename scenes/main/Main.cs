using Godot;
using System;

public partial class Main : Node
{
	[Export]
	public PackedScene EnemyScene { get; set; }

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void OnEnemyTimerTimeout()
	{
		// Create a new instance of the Enemy scene.
		Enemy enemy = EnemyScene.Instantiate<Enemy>();

		// Choose a random location on the SpawnPath.
		// We store the reference to the SpawnLocation node.
		var enemySpawnLocation = GetNode<PathFollow3D>("SpawnPath/SpawnLocation");
		// And give it a random offset.
		enemySpawnLocation.ProgressRatio = GD.Randf();

		Vector3 playerPosition = GetNode<Player>("Player").Position;
		enemy.Initialize(enemySpawnLocation.Position, playerPosition);

		// Spawn the enemy by adding it to the Main scene.
		AddChild(enemy);
	}
}
