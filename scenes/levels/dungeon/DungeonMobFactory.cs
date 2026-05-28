using Godot;
using Godot.Collections;

[Tool]
[GlobalClass]
public partial class DungeonMobFactory : MobFactory
{
	// Keep enemy scenes as paths so the level only loads enemy variants it actually spawns.
	[Export] public Array<string> EnemyScenePaths { get; set; }

	public override PackedScene CreateEnemy(uint dungeonDepth)
	{
		string scenePath = EnemyScenePaths.PickRandom();
		if (string.IsNullOrEmpty(scenePath))
		{
			GD.PrintErr("Dungeon mob factory has an empty scene path.");
			return null;
		}

		// Ignore the global cache so enemies from old floors are not held after scene reloads.
		PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (scene == null)
		{
			GD.PrintErr($"Could not load dungeon enemy scene: {scenePath}");
		}

		return scene;
	}
}
