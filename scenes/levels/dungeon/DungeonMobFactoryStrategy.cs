using System;
using Godot;

[Tool]
[GlobalClass]
public partial class DungeonMobFactoryStrategy : MobFactoryStrategy
{
	public override PackedScene CreateEnemy(Random random, int level)
	{
		var paths = new string[] {
			"res://scenes/enemies/skeleton/skeleton_minion.tscn",
			"res://scenes/enemies/skeleton/skeleton_warrior.tscn",
		};
		return GD.Load<PackedScene>(paths[random.Next(0, paths.Length)]);
	}
}
