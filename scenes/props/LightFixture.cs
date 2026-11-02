using Godot;

/// <summary>
/// A lighting fixture prop (candles, torches, braziers, hearths) with authored flame effect
/// and light, toggleable via <see cref="IsLit"/> both in the Godot Editor Inspector and at runtime.
/// </summary>
[Tool, GlobalClass]
public partial class LightFixture : Node3D
{
	private bool _isLit = true;

	/// <summary>Whether this fixture is currently lit.</summary>
	[Export]
	public bool IsLit
	{
		get => _isLit;
		set
		{
			_isLit = value;
			if (IsNodeReady())
			{
				UpdateLitState();
			}
		}
	}

	/// <summary>Parent node holding the flame particles or single particle system.</summary>
	[Export]
	public Node3D Flame { get; set; }

	/// <summary>Flickering or steady omni light for this fixture.</summary>
	[Export]
	public OmniLight3D Light { get; set; }

	/// <summary>Optional component managing dark level visibility.</summary>
	[Export]
	public EnvironmentalLightComponent EnvironmentalLight { get; set; }

	public override void _Ready()
	{
		UpdateLitState();
	}

	private void UpdateLitState()
	{
		if (EnvironmentalLight != null)
		{
			EnvironmentalLight.SetAuthoredLit(_isLit);
		}
		else
		{
			if (Flame != null)
			{
				Flame.Visible = _isLit;
			}

			if (Light != null)
			{
				Light.Visible = _isLit;
			}
		}

		if (Flame != null)
		{
			SetParticlesEmitting(Flame, _isLit && Flame.Visible);
		}
	}

	private static void SetParticlesEmitting(Node node, bool emitting)
	{
		if (node is GpuParticles3D particles)
		{
			particles.Emitting = emitting;
		}

		foreach (Node child in node.GetChildren())
		{
			SetParticlesEmitting(child, emitting);
		}
	}
}
