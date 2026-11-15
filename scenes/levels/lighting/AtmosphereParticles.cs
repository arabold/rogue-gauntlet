using Godot;

/// <summary>Floor emission and population for any authored particle scene; native particle resources own appearance and motion.</summary>
[GlobalClass]
public partial class AtmosphereParticles : AtmosphereEffect
{
	[Export] public GpuParticles3D Particles { get; set; }
	[ExportGroup("Population")]
	[Export(PropertyHint.Range, "0,4,0.05,or_greater")]
	public float ParticlesPerTile
	{
		get;
		set { field = Mathf.Max(0, value); if (IsNodeReady()) Rebuild(); }
	} = .7f;
	[Export(PropertyHint.Range, "1,4096,1,or_greater")]
	public int MaxParticles
	{
		get;
		set { field = Mathf.Max(1, value); if (IsNodeReady()) Rebuild(); }
	} = 512;
	/// <summary>An optional stable cosmetic stream name; empty uses the shared emission field's simulation seed.</summary>
	[Export]
	public string SeedKey
	{
		get;
		set { field = value; if (IsNodeReady()) Rebuild(); }
	} = "";
	[ExportGroup("Placement")]
	/// <summary>Restricts emission to dry walkable floor for surface-bound effects such as drops and splashes.</summary>
	[Export]
	public bool DryFloorOnly
	{
		get;
		set { field = value; if (IsNodeReady()) Rebuild(); }
	}
	[Export]
	public float MinHeight
	{
		get;
		set { field = value; if (IsNodeReady()) Rebuild(); }
	} = .4f;
	[Export]
	public float MaxHeight
	{
		get;
		set { field = value; if (IsNodeReady()) Rebuild(); }
	} = 3.5f;
	[Export(PropertyHint.Range, "1,16,1")]
	public int SamplesPerTile
	{
		get;
		set { field = Mathf.Clamp(value, 1, 16); if (IsNodeReady()) Rebuild(); }
	} = 4;
	[Export(PropertyHint.Range, "0,0.5,0.01")]
	public float TileInset
	{
		get;
		set { field = Mathf.Clamp(value, 0, .5f); if (IsNodeReady()) Rebuild(); }
	} = .35f;

	private ParticleProcessMaterial _motion;
	private bool _populated;
	private bool _dirty = true;

	protected override bool Prepare(AtmosphereContext context)
	{
		if (Particles?.ProcessMaterial is not ParticleProcessMaterial)
		{
			GD.PushError($"{Name}: assign Particles with a ParticleProcessMaterial.");
			return false;
		}
		BuildPopulation();
		return true;
	}

	private void Rebuild()
	{
		_dirty = true;
		if (!IsActive) return;
		BuildPopulation();
		OnActiveChanged(true);
	}

	private void BuildPopulation()
	{
		if (Context == null || Particles?.ProcessMaterial is not ParticleProcessMaterial material) return;
		_populated = ParticlesPerTile > 0;
		if (_populated)
		{
			if (_motion == null)
			{
				_motion = (ParticleProcessMaterial)material.Duplicate();
				Particles.ProcessMaterial = _motion;
			}
			float min = Mathf.Min(MinHeight, MaxHeight), max = Mathf.Max(MinHeight, MaxHeight);
			var emission = Context.GetEmission(min, max, SamplesPerTile, TileInset, DryFloorOnly);
			_populated = emission.Count > 0;
			_motion.EmissionPointTexture = emission.Points;
			_motion.EmissionPointCount = emission.Count;
			Rect2 bounds = Context.Bounds;
			float bottom = Mathf.Min(-1, min - 1);
			Particles.GlobalTransform = Transform3D.Identity;
			Particles.ResetPhysicsInterpolation();
			Particles.VisibilityAabb = new(new(bounds.Position.X - 2, bottom, bounds.Position.Y - 2),
				new(bounds.Size.X + 4, max + 4 - bottom, bounds.Size.Y + 4));
			Particles.Amount = Mathf.Clamp(Mathf.CeilToInt(Context.TileCount * ParticlesPerTile), 1, MaxParticles);
			Particles.UseFixedSeed = true;
			Particles.Seed = string.IsNullOrEmpty(SeedKey) ? emission.Seed
				: (uint)GenerationRandom.DeriveSeed(Context.Seed, GenerationRandom.Cosmetic, SeedKey);
			Particles.Restart();
		}
		Particles.Visible = _populated;
		_dirty = false;
	}

	protected override void OnActiveChanged(bool active)
	{
		if (Particles == null) return;
		if (active && _dirty) BuildPopulation();
		Particles.Emitting = active && _populated;
		Particles.ProcessMode = active && _populated ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
	}

	protected override void Release()
	{
		if (_motion != null) _motion.EmissionPointTexture = null;
		_motion = null;
		_populated = false;
		_dirty = true;
	}
}