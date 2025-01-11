using Godot;
using System.Collections.Generic;

public partial class ItemSpawner : Node
{
	[Export] private PackedScene ItemScene;

	[Signal] public delegate void ItemSpawnedEventHandler(ItemBase item);

	public void SpawnItems(List<ItemSpawnPoint> spawnPoints)
	{
		foreach (var spawnPoint in spawnPoints)
		{
			if (ItemScene == null)
			{
				GD.PrintErr("Item scene is not assigned!");
				return;
			}

			var item = ItemScene.Instantiate<Node3D>();
			item.GlobalTransform = new Transform3D(Basis.Identity, spawnPoint.Position);
			item.RotateY(Mathf.DegToRad(spawnPoint.Rotation));
			AddChild(item);

			EmitSignal(SignalName.ItemSpawned, item);
		}
	}
}
