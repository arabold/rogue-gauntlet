using Godot;

/// <summary>
/// How the player's light burns in one mode (<see cref="PlayerLight.Lit"/> or <see cref="PlayerLight.Dark"/>): the
/// shadow-casting light's brightness, reach and flicker, and the object highlight that keeps monsters and loot readable.
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

	[Export(PropertyHint.Range, "0,16,0.01,or_greater")] public float HighlightEnergy { get; set; } = 1f;
	[Export(PropertyHint.Range, "0,64,0.1,or_greater")] public float HighlightRange { get; set; } = 30f;
	[Export(PropertyHint.ExpEasing, "attenuation")] public float HighlightAttenuation { get; set; }
	/// <summary>How far above the player the highlight hangs; higher spreads it wider and flatter.</summary>
	[Export(PropertyHint.Range, "0,20,0.1,or_greater")] public float HighlightHeight { get; set; } = 10f;
}