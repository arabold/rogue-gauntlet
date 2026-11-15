namespace RogueGauntlet.Tests;

using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Finishes vary shared surfaces without changing source geometry, collision or deliberate metal/gloss.</summary>
[TestSuite, RequireGodotRuntime]
public class DungeonSurfaceFinishTest
{
	[TestCase]
	public void FinishesKeepSharedMeshesAndAuthoredSpecialMaterialsIntact()
	{
		var sourceMaterial = new StandardMaterial3D { Roughness = .85f };
		var metal = new StandardMaterial3D { Metallic = .8f, Roughness = .3f };
		var glass = new StandardMaterial3D { Roughness = .1f };
		var source = new BoxMesh { Material = sourceMaterial };
		var library = new MeshLibrary();
		string[] names = ["floor_planks", "floor_stone", "floor_metal", "floor_glass", "unmatched"];
		for (int id = 0; id < names.Length; id++)
		{
			library.CreateItem(id);
			library.SetItemName(id, names[id]);
			library.SetItemMesh(id, id switch { 2 => new BoxMesh { Material = metal }, 3 => new BoxMesh { Material = glass }, _ => source });
		}
		var transform = new Transform3D(Basis.Identity, new Vector3(1, .2f, 3));
		var shape = new BoxShape3D();
		library.SetItemMeshTransform(0, transform);
		library.SetItemShapes(0, new Godot.Collections.Array { shape, Transform3D.Identity });
		var grid = new GridMap { MeshLibrary = library };
		var world = new Node();
		world.AddChild(grid);
		world.AddChild(new DungeonSurfaceFinish
		{
			GridMaps = [grid],
			Profiles = [
				new SurfaceFinishProfile { ItemPrefixes = ["floor_planks"], Roughness = .64f },
				new SurfaceFinishProfile { ItemPrefixes = ["floor_"], Roughness = .92f }
			]
		});
		TestFactories.AddToSceneTree(world);

		AssertObject(grid.MeshLibrary).IsNotSame(library);
		AssertFloat(((BaseMaterial3D)grid.MeshLibrary.GetItemMesh(0).SurfaceGetMaterial(0)).Roughness).IsEqual(.64f);
		AssertFloat(((BaseMaterial3D)grid.MeshLibrary.GetItemMesh(1).SurfaceGetMaterial(0)).Roughness).IsEqual(.92f);
		AssertFloat(sourceMaterial.Roughness).IsEqual(.85f);
		AssertObject(library.GetItemMesh(0)).IsSame(source);
		AssertThat(grid.MeshLibrary.GetItemMesh(0).GetAabb()).IsEqual(source.GetAabb());
		AssertThat(grid.MeshLibrary.GetItemMeshTransform(0)).IsEqual(transform);
		AssertObject(grid.MeshLibrary.GetItemShapes(0)[0].AsGodotObject()).IsSame(shape);
		AssertObject(grid.MeshLibrary.GetItemMesh(2).SurfaceGetMaterial(0)).IsSame(metal);
		AssertObject(grid.MeshLibrary.GetItemMesh(3).SurfaceGetMaterial(0)).IsSame(glass);
		AssertObject(grid.MeshLibrary.GetItemMesh(4)).IsSame(source);
	}
}