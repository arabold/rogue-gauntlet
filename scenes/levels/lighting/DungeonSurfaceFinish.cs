using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Applies authored surface finishes to private mesh-library copies before dungeon generation. Geometry, tile IDs,
/// collision and source materials stay intact; metal, deliberate gloss and custom shaders retain their authored finish.
/// </summary>
[GlobalClass]
public partial class DungeonSurfaceFinish : Node
{
	[Export] public GridMap[] GridMaps { get; set; } = [];
	/// <summary>The first matching profile wins, so specific finishes precede the stone baseline.</summary>
	[Export] public SurfaceFinishProfile[] Profiles { get; set; } = [];

	public override void _Ready()
	{
		var meshes = new Dictionary<(Mesh, SurfaceFinishProfile), Mesh>();
		var materials = new Dictionary<(BaseMaterial3D, SurfaceFinishProfile), BaseMaterial3D>();
		foreach (var grid in GridMaps)
		{
			if (grid?.MeshLibrary == null)
			{
				GD.PushError($"{Name}: assign each GridMap and its mesh library.");
				continue;
			}
			var library = (MeshLibrary)grid.MeshLibrary.Duplicate();
			foreach (int id in library.GetItemList())
			{
				var profile = Profiles.FirstOrDefault(profile => profile?.Matches(library.GetItemName(id)) == true);
				var source = library.GetItemMesh(id);
				if (profile == null || source == null) continue;
				if (!meshes.TryGetValue((source, profile), out var mesh))
				{
					mesh = (Mesh)source.Duplicate();
					for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
					{
						if (source.SurfaceGetMaterial(surface) is not BaseMaterial3D material
							|| material.Metallic > 0 || material.Roughness < .25f) continue;
						if (!materials.TryGetValue((material, profile), out var finish))
						{
							finish = (BaseMaterial3D)material.Duplicate();
							finish.Roughness = profile.Roughness;
							finish.MetallicSpecular = profile.Specular;
							materials.Add((material, profile), finish);
						}
						mesh.SurfaceSetMaterial(surface, finish);
					}
					meshes.Add((source, profile), mesh);
				}
				library.SetItemMesh(id, mesh);
			}
			grid.MeshLibrary = library;
		}
	}
}