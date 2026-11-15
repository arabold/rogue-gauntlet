using Godot;

/// <summary>
/// One player-light mode: broad normal-floor shadow light, a held light in darkness, or the small innate light in darkness.
/// The point light's brightness, reach and flicker remain separate from the fill's rendering layers and falloff.
/// </summary>
[GlobalClass]
public partial class PlayerLightProfile : Resource
{
	/// <summary>Brightness the flicker multiplies.</summary>
	[Export(PropertyHint.Range, "0,16,0.01,or_greater")] public float Energy { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,64,0.1,or_greater")] public float Range { get; set; } = 25f;
	[Export(PropertyHint.ExpEasing, "attenuation")] public float Attenuation { get; set; } = 1f;
	/// <summary>Unassigned holds the light steady.</summary>
	[Export] public FlickerProfile Flicker { get; set; }
	/// <summary>Normal-floor helper light can exclude air effects while carried lights retain their physical reach.</summary>
	[Export(PropertyHint.Layers3DRender)] public uint LightLayers { get; set; } = 4293919231;

	[Export(PropertyHint.Range, "0,16,0.01,or_greater")] public float HighlightEnergy { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,64,0.1,or_greater")] public float HighlightRange { get; set; } = 30f;
	[Export(PropertyHint.ExpEasing, "attenuation")] public float HighlightAttenuation { get; set; }
	[Export(PropertyHint.Layers3DRender)] public uint HighlightLayers { get; set; } = 4 | 8 | 32 | 64 | 128;
	/// <summary>How far above the player the highlight hangs; higher spreads it wider and flatter.</summary>
	[Export(PropertyHint.Range, "0,20,0.1,or_greater")] public float HighlightHeight { get; set; } = 10f;
}