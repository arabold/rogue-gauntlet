using Godot;

/// <summary>Limits sight while the observed player is on a dark floor without an equipped light.</summary>
[GlobalClass]
public partial class DarknessScreenEffectDriver : ScreenEffectDriver
{
	private PlayerLight _lighting;

	[Export]
	public PlayerLight Lighting
	{
		get => _lighting;
		set => SetSource(ref _lighting, value);
	}

	/// <summary>How long the view opens up after light returns.</summary>
	[Export(PropertyHint.Range, "0,1,0.01,suffix:s")] public float FadeOutTime { get; set; } = 0.25f;

	protected override void Bind()
	{
		Rebind(_lighting, light => light.UnlitDarknessChanged += OnDarknessChanged,
			light => light.UnlitDarknessChanged -= OnDarknessChanged);
		Effect?.FadeTo(IsInstanceValid(_lighting) && _lighting.IsInUnlitDarkness ? 1f : 0f, 0f);
	}

	private void OnDarknessChanged(bool isUnlit) => Effect?.FadeTo(isUnlit ? 1f : 0f, isUnlit ? 0f : FadeOutTime);
}