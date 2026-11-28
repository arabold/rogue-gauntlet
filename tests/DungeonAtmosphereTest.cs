namespace RogueGauntlet.Tests;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Effects compose independently while retaining deterministic placement, private materials and floor lifetime.</summary>
[TestSuite, RequireGodotRuntime]
public class DungeonAtmosphereTest
{
	[TestCase]
	public void LayersRemainDeterministicWhenEffectsAreAddedOrReordered()
	{
		var dustScene = TestFactories.Load<PackedScene>("res://scenes/levels/lighting/atmosphere_effects/dust_jungle.tscn");
		var glowScene = TestFactories.Load<PackedScene>("res://scenes/levels/lighting/atmosphere_effects/motes_jungle.tscn");
		var hazeScene = TestFactories.Load<PackedScene>("res://scenes/levels/lighting/atmosphere_effects/haze_jungle.tscn");
		var prototype = TestFactories.AddToSceneTree(dustScene.Instantiate<AtmosphereParticles>());
		var authoredMotion = (ParticleProcessMaterial)prototype.Particles.ProcessMaterial;
		var (level, floor) = CreateFloor();
		var air = level.Atmosphere;
		var profile = new AtmosphereProfile { Effects = [dustScene, glowScene] };
		air.Initialize(floor, profile, 23);
		var dust = Layers(air).Single(layer => layer.SeedKey.Length == 0);
		var glow = Layers(air).Single(layer => layer.SeedKey.Length > 0);
		var motion = (ParticleProcessMaterial)dust.Particles.ProcessMaterial;
		AssertObject(motion).IsNotSame(authoredMotion);
		AssertObject(authoredMotion.EmissionPointTexture).IsNull();
		AssertObject(motion.EmissionPointTexture).IsSame(((ParticleProcessMaterial)glow.Particles.ProcessMaterial).EmissionPointTexture);
		byte[] points = PointBytes(motion);
		uint dustSeed = dust.Particles.Seed, glowSeed = glow.Particles.Seed;
		CheckPlacement(motion, floor.Map, dust.MinHeight, dust.MaxHeight);

		profile.Effects = [glowScene, hazeScene, dustScene];
		air.Initialize(floor, profile, 23);
		dust = Layers(air).Single(layer => layer.SeedKey.Length == 0);
		glow = Layers(air).Single(layer => layer.SeedKey.Length > 0);
		motion = (ParticleProcessMaterial)dust.Particles.ProcessMaterial;
		AssertArray(PointBytes(motion)).IsEqual(points);
		AssertThat(dust.Particles.Seed).IsEqual(dustSeed);
		AssertThat(glow.Particles.Seed).IsEqual(glowSeed);

		glow.MinHeight = 4.2f;
		glow.MaxHeight = 6.2f;
		CheckPlacement((ParticleProcessMaterial)glow.Particles.ProcessMaterial, floor.Map, 4.2f, 6.2f);
		AssertArray(PointBytes(motion)).IsEqual(points);
	}

	[TestCase]
	public void ParticleOnlyEffectsSupportInspectorChangesAndDarknessWithoutMutatingTheirPrototype()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/levels/lighting/atmosphere_effects/dust_garrison.tscn");
		var prototype = TestFactories.AddToSceneTree(scene.Instantiate<AtmosphereParticles>());
		prototype.Enabled = false;
		var authoredMotion = (ParticleProcessMaterial)prototype.Particles.ProcessMaterial;
		var disabledScene = new PackedScene();
		AssertThat(disabledScene.Pack(prototype)).IsEqual(Error.Ok);
		var (level, floor) = CreateFloor();
		var air = level.Atmosphere;
		air.Initialize(floor, new AtmosphereProfile { Effects = [disabledScene] }, 23);
		var layer = Layers(air).Single();
		AssertBool(layer.Visible || layer.Particles.Emitting).IsFalse();
		AssertObject(layer.Particles.ProcessMaterial).IsSame(authoredMotion);
		layer.Enabled = true;
		AssertBool(layer.Visible && layer.Particles.Emitting).IsTrue();
		AssertObject(layer.Particles.ProcessMaterial).IsNotSame(authoredMotion);
		AssertObject(authoredMotion.EmissionPointTexture).IsNull();
		layer.MaxParticles = 3;
		layer.ParticlesPerTile = 4;
		AssertInt(layer.Particles.Amount).IsEqual(3);
		layer.ParticlesPerTile = 0;
		AssertBool(layer.Particles.Visible || layer.Particles.Emitting).IsFalse();
		layer.ParticlesPerTile = 1;
		AssertBool(layer.Particles.Visible && layer.Particles.Emitting).IsTrue();
		level.Lighting.IsDark = true;
		layer.ParticlesPerTile = .25f;
		AssertBool(layer.IsVisibleInTree() || layer.Particles.Emitting).IsFalse();
		level.Lighting.IsDark = false;
		AssertBool(air.Visible && layer.Particles.Emitting).IsTrue();
		var runtimeMotion = (ParticleProcessMaterial)layer.Particles.ProcessMaterial;
		air.Clear();
		AssertBool(GodotObject.IsInstanceValid(layer)).IsFalse();
		AssertObject(runtimeMotion.EmissionPointTexture).IsNull();
		AssertInt(air.GetChildCount()).IsEqual(0);
	}

	[TestCase]
	public void HazeOnlyAndEmptyPresetsFollowFloorLifetime()
	{
		var scene = TestFactories.Load<PackedScene>("res://scenes/levels/lighting/atmosphere_effects/haze_garrison.tscn");
		var prototype = TestFactories.AddToSceneTree(scene.Instantiate<AtmosphereHaze>());
		var authored = (ShaderMaterial)prototype.Volume.Material;
		var (level, floor) = CreateFloor();
		var air = level.Atmosphere;
		var profile = new AtmosphereProfile { Effects = [scene] };
		air.Initialize(floor, profile, 23);
		var haze = air.GetChildren().OfType<AtmosphereHaze>().Single();
		AssertBool(haze.Volume.IsVisibleInTree()).IsTrue();
		AssertObject(haze.Volume.Material).IsNotSame(authored);
		var material = (ShaderMaterial)haze.Volume.Material;
		using var mask = material.GetShaderParameter("floor_mask").As<ImageTexture>().GetImage();
		AssertFloat(mask.GetPixel(1, 1).R).IsEqual(1);
		AssertFloat(mask.GetPixel(2, 2).R).IsEqual(0);
		haze.Height = 1.2f;
		AssertFloat(haze.Volume.Size.Y).IsEqualApprox(1.2f, .001f);
		haze.Enabled = false;
		AssertBool(haze.Volume.IsVisibleInTree()).IsFalse();
		haze.Enabled = true;
		level.Lighting.IsDark = true;
		AssertBool(haze.Volume.IsVisibleInTree() || level.Lighting.Environment.VolumetricFogEnabled).IsFalse();
		level.Lighting.IsDark = false;
		AssertBool(haze.Volume.IsVisibleInTree()).IsTrue();
		var replacement = new MapData(6, 6);
		replacement.SetTile(4, 1, MapTile.Room);
		air.Initialize(new GeneratedFloor(replacement, level.MapGenerator, level.MapGenerator.DecorationGridMap), profile, 42);
		haze = air.GetChildren().OfType<AtmosphereHaze>().Single();
		using var replaced = ((ShaderMaterial)haze.Volume.Material).GetShaderParameter("floor_mask").As<ImageTexture>().GetImage();
		AssertFloat(replaced.GetPixel(1, 1).R).IsEqual(0);
		AssertFloat(replaced.GetPixel(4, 1).R).IsEqual(1);
		air.Initialize(floor, new AtmosphereProfile(), 23);
		AssertBool(air.Visible).IsFalse();
		AssertInt(air.GetChildCount()).IsEqual(0);
		air.Initialize(floor, null, 23);
		AssertBool(air.Visible).IsFalse();
	}

	[TestCase]
	public void PrisonBloodStaysInEligibleRoomsAndReturnsAtTheSameSpots()
	{
		var (level, floor) = CreateFloor();
		string[] types = ["prison", "interrogation", "guardroom", "cell"];
		for (int id = 0; id < types.Length; id++)
		{
			var room = new RoomRegion(id) { TypeId = types[id] };
			for (int z = 1; z <= 2; z++)
			{
				var tile = new Vector2I(id + 1, z);
				floor.Map.SetTile(tile.X, tile.Y, MapTile.Room);
				floor.AssignTile(tile, id);
				room.Tiles.Add(tile);
			}
			var doorway = new Vector2I(id + 1, 3);
			floor.Map.SetConnector(doorway.X, doorway.Y, [Vector2I.Down]);
			floor.Map.SetTile(doorway.X, doorway.Y + 1, MapTile.Corridor);
			room.ConnectorTiles.Add(doorway);
			floor.AddRoom(room);
		}
		var corridor = new Vector2I(0, 4);
		floor.Map.SetTile(corridor.X, corridor.Y, MapTile.Corridor);
		floor.Map.SetTile(corridor.X, corridor.Y + 1, MapTile.Corridor);
		floor.Map.SealCorridorEdge(corridor, Vector2I.Right);
		var profile = TestFactories.Load<AtmosphereProfile>("res://scenes/levels/lighting/atmosphere_profiles/prison.tres");
		var air = level.Atmosphere;
		level.Lighting.IsDark = true;
		air.Initialize(floor, profile, 23);
		var splatters = BloodMarks("blood-splatters");
		var drags = BloodMarks("blood-drags");
		AssertArray(splatters.Select(mark => floor.RoomIdAt(mark.GlobalPosition)).Order().ToArray()).IsEqual(new[] { 0, 1, 3 });
		AssertArray(drags.Select(mark => floor.RoomIdAt(mark.GlobalPosition)).Order().ToArray()).IsEqual(new[] { 0, 1 });
		foreach (var mark in drags)
		{
			AssertInt(floor.Map.WorldToTile(mark.GlobalPosition).Y).IsEqual(2);
			AssertFloat(mark.GlobalBasis.Z.Dot(Vector3.Back)).IsEqualApprox(1f, .001f);
		}
		var scuffs = air.GetChildren().OfType<AtmosphereSurfaceMarks>().Single(layer => layer.SeedKey == "floor-scuffs");
		scuffs.MarksPerTile = 1f;
		var scuffMarks = scuffs.GetChildren().OfType<SurfaceMark>().ToArray();
		var corridorMark = scuffMarks.Single(mark => floor.Map.WorldToTile(mark.GlobalPosition) == corridor);
		AssertFloat(corridorMark.GlobalBasis.Z.Dot(Vector3.Back)).IsEqualApprox(1f, .001f);
		AssertInt(splatters.Select(mark => mark.TextureAlbedo.ResourcePath).Distinct().Count()).IsEqual(splatters.Length);
		AssertInt(drags.Select(mark => mark.TextureAlbedo.ResourcePath).Distinct().Count()).IsEqual(drags.Length);
		AssertBool(drags.All(drag => scuffMarks.Any(scuff => scuff.TextureAlbedo == drag.TextureAlbedo
			&& scuff.Modulate != drag.Modulate))).IsTrue();
		var blood = splatters.Concat(drags).ToArray();
		var placements = blood.Select(mark => mark.GlobalTransform).ToArray();
		var masks = blood.Select(mark => mark.TextureAlbedo.ResourcePath).ToArray();
		level.Lighting.DisabledEffects = EnvironmentalEffect.SurfaceMarks;
		AssertBool(blood.Any(mark => mark.IsVisibleInTree())).IsFalse();
		scuffs.MaximumCount = 1;
		level.Lighting.DisabledEffects = EnvironmentalEffect.None;
		AssertInt(scuffs.GetChildren().OfType<SurfaceMark>().Count(mark => mark.IsVisibleInTree())).IsEqual(1);
		air.Initialize(floor, profile, 23);
		var restored = BloodMarks("blood-splatters").Concat(BloodMarks("blood-drags")).ToArray();
		AssertArray(restored.Select(mark => mark.GlobalTransform).ToArray()).IsEqual(placements);
		AssertArray(restored.Select(mark => mark.TextureAlbedo.ResourcePath).ToArray()).IsEqual(masks);

		SurfaceMark[] BloodMarks(string seedKey) => air.GetChildren().OfType<AtmosphereSurfaceMarks>()
			.Single(layer => layer.SeedKey == seedKey).GetChildren().OfType<SurfaceMark>()
			.Where(mark => mark.IsVisibleInTree() && mark.TextureAlbedo != null).ToArray();
	}

	[TestCase]
	public void CpuAndGpuEmissionConsumersShareTheSameDryFloorPositions()
	{
		var map = new MapData(4, 4);
		map.SetTile(1, 1, MapTile.Room);
		map.SetTile(2, 2, MapTile.Chasm);
		using var context = new AtmosphereContext(map, 23);
		var field = context.GetEmission(.025f, .025f, 4, .25f, true);
		AssertInt(field.Count).IsEqual(4);
		using var image = field.Points.GetImage();
		for (int i = 0; i < field.Count; i++)
		{
			Vector3 point = field.Positions[i];
			Color pixel = image.GetPixel(i, 0);
			AssertThat(new Vector3(pixel.R, pixel.G, pixel.B)).IsEqual(point);
			AssertThat(map.WorldToTile(point)).IsEqual(new Vector2I(1, 1));
		}
	}

	[TestCase]
	public void WallDustFallsOnlyDownWallsTheCameraFaces()
	{
		var (level, floor) = CreateFloor();
		var air = level.Atmosphere;
		air.Initialize(floor, new AtmosphereProfile
		{
			Effects = [TestFactories.Load<PackedScene>("res://scenes/levels/lighting/atmosphere_effects/dust_walls.tscn")]
		}, 23);
		var settling = air.GetChildren().OfType<AtmosphereWallDust>().Single();
		// The room tile is walled on all four sides; the camera looks at it from the south, as the game's camera does.
		Vector3 room = floor.Map.TileToWorld(1, 1);
		// Uninterpolated, so its view is where it stands without waiting for a frame.
		var camera = TestFactories.AddToSceneTree(new Camera3D { Current = true, PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off });
		camera.LookAtFromPosition(room + new Vector3(0, 10, 7), room);

		var walls = new HashSet<Vector2>();
		for (int burst = 0; burst < 12; burst++)
		{
			settling._Process(settling.MaxInterval);
			foreach (var dust in settling.GetChildren().OfType<WallDust>().Where(dust => dust.Emitting))
			{
				Vector2 offset = (dust.GlobalPosition - room).Xz();
				AssertThat(floor.Map.WorldToTile(dust.GlobalPosition)).IsEqual(new Vector2I(1, 1));
				AssertFloat(offset.Y).OverrideFailureMessage("Dust fell behind the south wall, out of the camera's sight.").IsLessEqual(.01f);
				walls.Add(offset.Normalized().Round());
			}
		}
		AssertInt(walls.Count).OverrideFailureMessage("Dust kept to a single wall.").IsGreater(1);
	}

	private static AtmosphereParticles[] Layers(DungeonAtmosphere air) => air.GetChildren().OfType<AtmosphereParticles>().ToArray();

	private static (Level, GeneratedFloor) CreateFloor()
	{
		var level = TestFactories.Load<PackedScene>(TestFactories.LevelScenePath).Instantiate<Level>();
		TestFactories.AddToSceneTree(level);
		var map = new MapData(6, 6);
		map.SetTile(1, 1, MapTile.Room);
		map.SetTile(3, 4, MapTile.Chasm);
		map.SetTile(2, 2, MapTile.Wall);
		return (level, new GeneratedFloor(map, level.MapGenerator, level.MapGenerator.DecorationGridMap));
	}

	private static byte[] PointBytes(ParticleProcessMaterial motion)
	{
		using var points = motion.EmissionPointTexture.GetImage();
		return points.GetData();
	}

	private static void CheckPlacement(ParticleProcessMaterial motion, MapData map, float min, float max)
	{
		using var points = motion.EmissionPointTexture.GetImage();
		for (int i = 0; i < points.GetWidth(); i++)
		{
			Color point = points.GetPixel(i, 0);
			Vector2I tile = map.WorldToTile(new(point.R, point.G, point.B));
			AssertBool(map.IsExplorable(tile.X, tile.Y)).OverrideFailureMessage("Air was emitted over a wall or void.").IsTrue();
			AssertFloat(point.G).IsBetween(min, max);
		}
	}
}