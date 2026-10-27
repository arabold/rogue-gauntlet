using Godot;

public enum SurfaceAppearanceKind { Skin, Cloak, Transition }

/// <summary>Immutable surface template. Controllers own private materials and transition amounts.</summary>
[Tool]
[GlobalClass]
public partial class SurfaceAppearanceProfile : Resource
{
	[Export] public ShaderMaterial Material { get; set; }
	[Export] public SurfaceAppearanceKind Kind { get; set; }
	[Export] public int Priority { get; set; }
}