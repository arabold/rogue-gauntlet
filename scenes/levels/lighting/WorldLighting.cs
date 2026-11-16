using Godot;

/// <summary>
/// A world's shared illumination. Darkness removes ambient light, reflections and haze; the hero's light dims and
/// fixtures go out by following <see cref="DarknessChanged"/>. The game, arena and workbenches use the same environment.
/// </summary>
[GlobalClass]
public partial class WorldLighting : WorldEnvironment
{
	[Signal] public delegate void DarknessChangedEventHandler(bool isDark);

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

	/// <summary>Enables the authored atmospheric fog while respecting the darkness setting.</summary>
	[Export]
	public bool FogEnabled
	{
		get;
		set { field = value; if (IsNodeReady()) ApplyLighting(); }
	} = true;

	private float _normalAmbient;
	private Environment.ReflectionSource _normalReflections;
	private bool _normalFog;
	private bool _normalVolumetricFog;

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
		}
		ApplyLighting();
	}

	private void ApplyLighting()
	{
		if (Environment != null)
		{
			Environment.AmbientLightEnergy = IsDark ? 0f : _normalAmbient;
			Environment.ReflectedLightSource = IsDark ? Environment.ReflectionSource.Disabled : _normalReflections;
			Environment.FogEnabled = !IsDark && FogEnabled && _normalFog;
			Environment.VolumetricFogEnabled = !IsDark && FogEnabled && _normalVolumetricFog;
		}
	}
}