using Godot;

/// <summary>Creates an authored faceted crystal mesh for a scene's visual layer.</summary>
public partial class FacetedSpikeInstance : MeshInstance3D
{
	[Export] public Vector3 Size { get; set; } = new(.25f, 1.6f, .25f);
	[Export] public float ShapeSeed { get; set; } = 1f;
	public override void _Ready() => Mesh = FacetedSpikeMesh.Create(Size, ShapeSeed, MaterialOverride);
}
