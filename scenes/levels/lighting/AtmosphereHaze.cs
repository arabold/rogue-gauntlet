using Godot;

/// <summary>A reusable masked fog volume. Appearance and motion are native ShaderMaterial parameters in its scene.</summary>
[GlobalClass]
public partial class AtmosphereHaze : AtmosphereEffect
{
	[Export] public FogVolume Volume { get; set; }
	[Export(PropertyHint.Range, "0.5,10,0.1,or_greater")]
	public float Height
	{
		get;
		set { field = Mathf.Max(.1f, value); if (IsNodeReady()) UpdateBounds(); }
	} = 3.5f;
	[Export]
	public bool Animate
	{
		get;
		set { field = value; if (IsNodeReady()) SetProcess(IsActive && value); }
	} = true;

	private ShaderMaterial _material;
	private float _elapsed;
	private static readonly StringName FloorMask = "floor_mask", MapOrigin = "map_origin", MapExtent = "map_extent",
		Phase = "phase", Elapsed = "elapsed";

	protected override bool Prepare(AtmosphereContext context)
	{
		if (Volume?.Material is not ShaderMaterial material)
		{
			GD.PushError($"{Name}: assign a shader-backed Volume.");
			return false;
		}
		_material = (ShaderMaterial)material.Duplicate();
		Volume.Material = _material;
		UpdateBounds();
		_material.SetShaderParameter(FloorMask, context.FloorMask);
		_material.SetShaderParameter(MapOrigin, context.Bounds.Position);
		_material.SetShaderParameter(MapExtent, context.Bounds.Size);
		_material.SetShaderParameter(Phase, context.Phase);
		_material.SetShaderParameter(Elapsed, 0f);
		return true;
	}

	private void UpdateBounds()
	{
		if (Context == null || Volume == null) return;
		Rect2 bounds = Context.Bounds;
		Volume.GlobalPosition = new(bounds.GetCenter().X, .05f + Height / 2, bounds.GetCenter().Y);
		Volume.Size = new(bounds.Size.X, Height, bounds.Size.Y);
		Volume.ResetPhysicsInterpolation();
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		_material.SetShaderParameter(Elapsed, _elapsed);
	}

	protected override void OnActiveChanged(bool active) => SetProcess(active && Animate);

	protected override void Release()
	{
		_material.SetShaderParameter(FloorMask, default);
		_material.SetShaderParameter(Elapsed, 0f);
		_material = null;
		_elapsed = 0f;
	}
}