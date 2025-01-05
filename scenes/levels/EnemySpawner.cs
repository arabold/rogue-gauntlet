using Godot;
using System.Collections.Generic;

public partial class EnemySpawner : Node
{
    [Export] private PackedScene EnemyScene;

    [Signal] public delegate void EnemySpawnedEventHandler(Enemy enemy);

    public void SpawnEnemies(List<EnemySpawnPoint> spawnPoints)
    {
        foreach (var spawnPoint in spawnPoints)
        {
            if (EnemyScene == null)
            {
                GD.PrintErr("Enemy scene is not assigned!");
                return;
            }

            var enemy = EnemyScene.Instantiate<Node3D>();
            enemy.GlobalTransform = new Transform3D(Basis.Identity, spawnPoint.Position);
            AddChild(enemy);

            EmitSignal(SignalName.EnemySpawned, enemy);
        }
    }
}
