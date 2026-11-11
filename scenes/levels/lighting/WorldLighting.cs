using Godot;

/// <summary>
/// A world's illumination: the game's shared environment and whether the lights are out. Dark turns the ambient light
/// off; the hero's light dims and fixtures go out by following <see cref="DarknessChanged"/>. The game, the harness arena
/// and the workbenches all light through it, so a scene looks the same everywhere.
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
			ApplyAmbient();
			EmitSignal(SignalName.DarknessChanged, value);
		}
	}

	private float _normalAmbient;

	public override void _Ready()
	{
		if (Environment == null)
		{
			return;
		}

		// Own a runtime copy so darkness never mutates the shared environment resource.
		Environment = (Environment)Environment.Duplicate();
		_normalAmbient = Environment.AmbientLightEnergy;
		ApplyAmbient();
	}

	private void ApplyAmbient()
	{
		if (Environment != null)
		{
			Environment.AmbientLightEnergy = IsDark ? 0f : _normalAmbient;
		}
	}
}
