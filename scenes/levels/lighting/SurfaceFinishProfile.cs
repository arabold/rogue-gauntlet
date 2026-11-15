using Godot;
using System;

/// <summary>A dungeon surface's light response, selected by the mesh library's authored item-name prefixes.</summary>
[GlobalClass]
public partial class SurfaceFinishProfile : Resource
{
	[Export] public string[] ItemPrefixes { get; set; } = [];
	[Export(PropertyHint.Range, "0,1,0.01")] public float Roughness { get; set; } = .85f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Specular { get; set; } = .35f;

	public bool Matches(string itemName) => Array.Exists(ItemPrefixes, prefix =>
		!string.IsNullOrEmpty(prefix) && itemName.StartsWith(prefix, StringComparison.Ordinal));
}