using Godot;

/// <summary>Editor-authored effect categories used for independent visual diagnostics.</summary>
[System.Flags]
public enum EnvironmentalEffect
{
	None = 0,
	Fog = 1,
	SurfaceMarks = 2,
	Dust = 4,
	Drips = 8,
	Reflections = 16,
	Wind = 32
}

/// <summary>
/// A world's shared illumination. Darkness removes ambient light, reflections and haze. The hero's lighting and
/// fixtures respond to <see cref="DarknessChanged"/>.
/// </summary>
[GlobalClass]
public partial class WorldLighting : WorldEnvironment
{
	[Signal] public delegate void DarknessChangedEventHandler(bool isDark);
	[Signal] public delegate void EffectsChangedEventHandler();

	/// <summary>Whether the world's lights are out; debug changes last for this scene only.</summary>
	[Export]
	public bool IsDark
	{
		get;
		set
		{
			if (field == value) { return; }
			field = value;
			if (!IsNodeReady()) { return; }
			ApplyLighting();
			EmitSignal(SignalName.DarknessChanged, value);
		}
	}

	/// <summary>Temporary world-local overrides; authored effects retain their individual settings.</summary>
	[Export]
	public EnvironmentalEffect DisabledEffects
	{
		get;
		set
		{
			if (field == value) return;
			field = value;
			if (!IsNodeReady()) return;
			ApplyLighting();
			EmitSignal(SignalName.EffectsChanged);
		}
	}

	/// <summary>
	/// Multiplies the shared environment's ambient light for this world only, such as an open debug arena without the
	/// wall fixtures that light a dungeon floor. Darkness still removes it.
	/// </summary>
	[Export(PropertyHint.Range, "0,4,0.05")]
	public float AmbientScale
	{
		get;
		set
		{
			if (field == value) return;
			field = value;
			if (!IsNodeReady()) return;
			ApplyLighting();
		}
	} = 1f;

	public bool IsEffectEnabled(EnvironmentalEffect effect) => (DisabledEffects & effect) == 0;

	private float _normalAmbient;
	private Environment.ReflectionSource _normalReflections;
	private bool _normalFog;
	private bool _normalVolumetricFog;
	private bool _normalSsr;

	public override void _Ready()
	{
		if (Environment != null)
		{
			// Own a runtime copy so darkness never mutates the shared environment resource.
			Environment = (Environment)Environment.Duplicate();
			_normalAmbient = Environment.AmbientLightEnergy;
			_normalReflections = Environment.ReflectedLightSource;
			_normalFog = Environment.FogEnabled;
			_normalVolumetricFog = Environment.VolumetricFogEnabled;
			_normalSsr = Environment.SsrEnabled;
		}
		ApplyLighting();
	}

	private void ApplyLighting()
	{
		if (Environment != null)
		{
			Environment.AmbientLightEnergy = IsDark ? 0f : _normalAmbient * AmbientScale;
			bool reflections = !IsDark && IsEffectEnabled(EnvironmentalEffect.Reflections);
			bool fog = !IsDark && IsEffectEnabled(EnvironmentalEffect.Fog);
			Environment.ReflectedLightSource = reflections ? _normalReflections : Environment.ReflectionSource.Disabled;
			Environment.SsrEnabled = reflections && _normalSsr;
			Environment.FogEnabled = fog && _normalFog;
			Environment.VolumetricFogEnabled = fog && _normalVolumetricFog;
		}
	}
}