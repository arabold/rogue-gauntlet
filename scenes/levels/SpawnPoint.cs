using System.Xml.Schema;
using Godot;

public partial class SpawnPoint : Node3D
{
	/// <summary>
	/// The type of object to spawn (e.g. an enemy scene)
	/// </summary>
	[Export] public PackedScene Scene;
	/// <summary>
	/// Optional timer to control the spawn rate
	/// </summary>
	[Export] public Timer Timer { get; set; }
	/// <summary>
	/// Whether to spawn immediately on scene load
	/// </summary>
	[Export] public bool SpawnOnStart { get; set; } = true;

	public override void _Ready()
	{
		if (Timer != null)
		{
			Timer.Timeout += Spawn;
		}
		else if (SpawnOnStart)
		{
			Spawn();
		}
	}

	/// <summary>
	/// Spawns an objecct at the spawn point
	/// </summary>
	public void Spawn()
	{
		if (Scene == null)
		{
			GD.PrintErr("Scene is null");
			return;
		}

		var scene = Scene.Instantiate() as Node3D;
		AddChild(scene);
	}
}
