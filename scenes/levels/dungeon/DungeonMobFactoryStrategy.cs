using System;
using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonMobFactoryStrategy : MobFactoryStrategy
{
	public override PackedScene CreateEnemy(int dungeonLevel)
	{
		var paths = new Array<string>{
			"res://scenes/enemies/skeleton/skeleton_minion.tscn",
			"res://scenes/enemies/skeleton/skeleton_warrior.tscn",
		};
		return GD.Load<PackedScene>(paths.PickRandom());
	}
}
