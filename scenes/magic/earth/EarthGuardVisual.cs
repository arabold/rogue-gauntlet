using Godot;
using System.Collections.Generic;

/// <summary>Builds and animates either rotating stone panels or floating spiked armor.</summary>
public partial class EarthGuardVisual : Node
{
	[Export] public Node3D VisualRoot { get; set; }
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public bool Spiked { get; set; }
	[Export] public float PanelRotationSpeed { get; set; } = .8f;
	private readonly List<Vector3> _positions = [];
	private readonly List<Basis> _bases = [];
	private Node3D _pieces;
	private int _level = 1;
	public void ConfigureLevel(int level) => _level = Mathf.Clamp(level, 1, 3);
	public override void _Ready()
	{
		if (VisualRoot == null || Timeline == null) { GD.PushError("Earth guard requires a visual root and timeline."); SetProcess(false); return; }
	}
	/// <summary>Creates guard pieces on the explicitly assigned visual root.</summary>
	public void Build()
	{
		if (VisualRoot == null || Timeline == null) return;
		var stone = new StandardMaterial3D { AlbedoColor = Spiked ? new Color(.095f, .07f, .12f) : new Color(.33f, .26f, .19f), Roughness = .9f };
		_pieces = new Node3D { Name = Spiked ? "ArmorSpikes" : "StonePanels" };
		VisualRoot.AddChild(_pieces);
		int count = Spiked ? 12 : 6;
		for (int i = 0; i < count; i++)
		{
			float angle = Mathf.Tau * i / count;
			Vector3 radial = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
			if (!Spiked)
			{
				float height = 1.05f + _level * .45f;
				_pieces.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.8f, height, .2f), Material = stone }, Position = radial * 1.25f + Vector3.Up * (height * .5f + .12f), Rotation = new Vector3(0, -angle + Mathf.Pi / 2, 0) });
			}
			else
			{
				float height = .6f + .12f * _level;
				var spike = new MeshInstance3D { Mesh = FacetedSpikeMesh.Create(new Vector3(.25f, height, .25f), angle, stone), Position = radial * .8f + Vector3.Up * (.7f + (i % 3) * .32f), Basis = new Basis(Vector3.Up.Cross(radial), 1.1f) };
				_pieces.AddChild(spike); _positions.Add(spike.Position); _bases.Add(spike.Basis);
			}
		}
		if (!Spiked) AddScrapeDust();
	}
	private void AddScrapeDust()
	{
		var process = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = .18f, Direction = Vector3.Up, Spread = 80, InitialVelocityMin = .08f, InitialVelocityMax = .35f, Gravity = new Vector3(0, -.15f, 0), ScaleMin = .14f, ScaleMax = .35f };
		var mesh = new QuadMesh { Size = Vector2.One, Material = EarthDust.Material(.4f) };
		for (int i = 0; i < 2; i++)
			_pieces.AddChild(new GpuParticles3D { Position = new Vector3(i == 0 ? 1.25f : -1.25f, .1f, 0), Amount = 12, Lifetime = .9, LocalCoords = false, ProcessMaterial = process, DrawPass1 = mesh, Emitting = true });
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		if (!Spiked) { _pieces.RotateY((float)delta * PanelRotationSpeed); return; }
		for (int i = 0; i < _positions.Count; i++)
		{
			var spike = (Node3D)_pieces.GetChild(i);
			float phase = i * 2.399f, time = Timeline.Elapsed;
			float wobble = Mathf.Sin(time * (1.1f + (i % 4) * .17f) + phase);
			spike.Position = _positions[i] + Vector3.Up * (.07f * wobble);
			spike.Basis = _bases[i] * new Basis(Vector3.Right, .06f * Mathf.Sin(time * 1.3f + phase));
			float pulse = 1 + .09f * Mathf.Sin(time * (1.5f + (i % 3) * .23f) + phase * 1.3f);
			spike.Scale = Vector3.One * pulse;
		}
	}
}