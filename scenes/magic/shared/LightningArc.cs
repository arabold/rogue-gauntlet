using Godot;

/// <summary>Animated camera-facing lightning ribbon between two captured points.</summary>
public partial class LightningArc : MeshInstance3D
{
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public MeshInstance3D Impact { get; set; }
	private const float DefaultWidth = .5f;
	[Export] public float Width { get; set; } = DefaultWidth;
	[Export] public float Jaggedness { get; set; } = .28f;
	private Vector3 _start, _target, _end;
	private ulong _seed;
	private float _age;
	private int _frame;
	private readonly RandomNumberGenerator _random = new();
	private readonly Vector3[] _points = new Vector3[17];

	/// <summary>Sets the local endpoint and cosmetic seed before adding the arc to the tree.</summary>
	public void Configure(Vector3 start, Vector3 end, ulong seed)
	{
		_start = start;
		_target = end;
		_seed = seed;
	}
	public override void _Ready()
	{
		GlobalPosition = _start;
		_end = ToLocal(_target);
		if (Impact != null) Impact.Position = _end;
		// Wider (higher-level) bolts flash brighter.
		Timeline?.SetLightMultiplier(Mathf.Clamp(Width / DefaultWidth, .5f, 2f));
		Rebuild();
	}
	public override void _Process(double delta)
	{
		_age += (float)delta;
		if (_age < .07f) return;
		_age = 0; _frame++; Rebuild();
	}
	private void Rebuild()
	{
		const int steps = 16;
		_random.Seed = _seed + (ulong)_frame * 7919;
		Vector3 direction = _end.Normalized();
		if (direction.LengthSquared() < .001f) direction = Vector3.Right;
		Vector3 view = GetViewport().GetCamera3D()?.GlobalTransform.Basis.Z ?? Vector3.Back;
		Vector3 side = direction.Cross(view).Normalized();
		if (side.LengthSquared() < .001f) side = Vector3.Up;
		Vector3 other = direction.Cross(side).Normalized();
		var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
		Vector3 previous = Vector3.Zero;

		for (int i = 1; i <= steps; i++)
		{
			float t = i / (float)steps;
			float envelope = Mathf.Sin(Mathf.Pi * t);
			Vector3 point = _end * t + (side * _random.RandfRange(-1, 1) + other * _random.RandfRange(-.5f, .5f)) * Jaggedness * envelope;
			AddVertex(surface, previous - side * Width, (i - 1f) / steps, 0);
			AddVertex(surface, previous + side * Width, (i - 1f) / steps, 1);
			AddVertex(surface, point - side * Width, t, 0);
			AddVertex(surface, point - side * Width, t, 0);
			AddVertex(surface, previous + side * Width, (i - 1f) / steps, 1);
			AddVertex(surface, point + side * Width, t, 1);
			previous = point; _points[i] = point;
		}
		for (int i = 3; i < steps - 2; i += 4)
		{
			float sign = _random.Randf() > .5f ? 1 : -1;
			Vector3 fork = _points[i] + direction * _random.RandfRange(.35f, .85f) + side * sign * _random.RandfRange(.35f, .8f);
			Vector3 mid = _points[i].Lerp(fork, .55f) + side * sign * _random.RandfRange(-.16f, .16f);
			AddBranch(surface, _points[i], mid, side * Width * .42f);
			AddBranch(surface, mid, fork, side * Width * .25f);
		}
		Mesh = surface.Commit();
	}
	private static void AddBranch(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 side)
	{
		AddVertex(surface, a - side, 0, 0); AddVertex(surface, a + side, 0, 1); AddVertex(surface, b - side, 1, 0);
		AddVertex(surface, b - side, 1, 0); AddVertex(surface, a + side, 0, 1); AddVertex(surface, b + side, 1, 1);
	}
	private static void AddVertex(SurfaceTool surface, Vector3 point, float u, float v)
	{
		surface.SetUV(new Vector2(u, v)); surface.AddVertex(point);
	}
}