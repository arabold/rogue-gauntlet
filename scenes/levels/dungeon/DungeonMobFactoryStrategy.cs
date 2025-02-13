using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonMobFactoryStrategy : MobFactoryStrategy
{
	[Export] public Array<PackedScene> EnemieScenes { get; set; }

	public override PackedScene CreateEnemy(uint dungeonDepth)
	{
		var scene = EnemieScenes.PickRandom();
		return scene;
	}
}
