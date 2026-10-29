using Godot;
using System.Collections.Generic;

/// <summary>Self-contained visual studies for earth spells; no combat or equipment behavior.</summary>
public partial class EarthSpellVisual : Node3D, IAreaSpellVisual, IGuardVisual
{
	public enum EarthKind { Rockfall, Tremor, Spikes, StoneSkin, Obsidian }
	[Export] public EarthKind Kind { get; set; }
	[Export] public VisualEffectTimeline Timeline { get; set; }
	[Export] public EarthGuardVisual Guard { get; set; }
	[Export] public FallingBodyMotion Fall { get; set; }
	public float StartDelay { get => Timeline.StartDelay; set => Timeline.StartDelay = value; }
	public bool Cosmetic { get; set; }
	[Export] public Shader WaveShader { get; set; }
	[Export] public Shader CrackShader { get; set; }
	[Export] public Shader AreaStoneShader { get; set; }
	[Export] public Shader AreaRingShader { get; set; }
	[Export] public Shader AreaDustShader { get; set; }
	private readonly List<Node3D> _rocks = [];
	private readonly List<Vector3> _rockScales = [];
	private readonly List<MeshInstance3D> _rings = [];
	private Node3D _falling;
	private Node3D _fragments;
	private readonly List<GpuParticles3D> _dust = [];
	private StandardMaterial3D _glow;
	private ShaderMaterial _waveMaterial;
	private ShaderMaterial _crackMaterial;
	private ShaderMaterial _areaStoneMaterial, _areaRingMaterial, _areaDustMaterial;
	private StandardMaterial3D _fragmentMaterial;
	private QuadMesh _dustMesh;
	private SphereMesh _stoneMesh;
	private MeshInstance3D _wave;
	private bool _started;
	private bool _landed;
	private int _level = 1;
	private float _areaRadius;
	public bool IsGuard => Kind is EarthKind.StoneSkin or EarthKind.Obsidian;
	public bool ConfigureGuard(int level, ulong cosmeticSeed)
	{
		if (!IsGuard || Guard == null || Timeline?.Sustained != true) return false;
		ConfigureLevel(level); return true;
	}
	public void FlashGuard() { }
	public AreaVisibility Visibility { get; private set; }
	public float PreviewRadius => _areaRadius > 0 ? _areaRadius : (Kind == EarthKind.Tremor ? 3.5f : 3f) + (_level - 1) * 1.5f;

	/// <summary>Sets the presentation tier before the scene enters the tree.</summary>
	public void ConfigureLevel(int level) { _level = Mathf.Clamp(level, 1, 3); Guard?.ConfigureLevel(_level); }
	/// <summary>Configure an earth area effect to the combat definition's radius before entering the tree.</summary>
	public bool ConfigureArea(int level, float radius)
	{
		if (Kind is not (EarthKind.Tremor or EarthKind.Spikes) || Timeline == null || Timeline.Sustained || !float.IsFinite(Timeline.Duration) || Timeline.Duration <= 0
			|| WaveShader == null || AreaStoneShader == null || AreaRingShader == null || AreaDustShader == null
			|| !float.IsFinite(radius) || radius <= 0) return false;
		ConfigureLevel(level); _areaRadius = radius; return true;
	}
	public void SetAreaVisibility(AreaVisibility visibility) => SetVisibility(visibility);
	public override void _Ready()
	{
		if (IsGuard) { Guard?.Build(); SetProcess(false); return; }
		if (StartDelay > 0) Visible = false;
		var stone = new StandardMaterial3D { AlbedoColor = new Color(.33f, .26f, .19f), Roughness = .9f };
		bool area = Kind is EarthKind.Tremor or EarthKind.Spikes;
		if (area && AreaStoneShader != null) _areaStoneMaterial = new ShaderMaterial { Shader = AreaStoneShader };
		if (area && AreaRingShader != null) _areaRingMaterial = new ShaderMaterial { Shader = AreaRingShader };
		if (area && AreaDustShader != null) _areaDustMaterial = new ShaderMaterial { Shader = AreaDustShader };
		Material stoneMaterial = _areaStoneMaterial ?? (Material)stone;
		_stoneMesh = new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 7, Rings = 4, Material = stoneMaterial };
		_dustMesh = new QuadMesh { Size = Vector2.One, Material = _areaDustMaterial ?? (Material)EarthDust.Material(.4f) };
		_glow = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.95f, .55f, .2f, .8f), EmissionEnabled = true, Emission = new Color(1, .4f, .08f), EmissionEnergyMultiplier = 1.2f };
		if (Kind == EarthKind.Rockfall)
		{
			_falling = new Node3D(); AddChild(_falling);
			Fall.ConfigureBody(_falling); Fall.Impact += OnRockImpact;
			AddRock(_falling, stone, new Vector3(0, 0, 0), new Vector3(1.1f, 1.35f, 1f), 0);
			AddRing(1.2f); AddWave(2.8f); AddDust(Vector3.Zero, 1.2f, 48); AddCracks(); AddFragments();
		}
		else if (Kind is EarthKind.Tremor or EarthKind.Spikes)
		{
			AddRing(PreviewRadius * .48f); AddRing(PreviewRadius * .8f); AddWave(PreviewRadius); AddDust(Vector3.Zero, PreviewRadius * .65f, 60 + _level * 20);
			int count = Kind == EarthKind.Spikes ? 10 + _level * 5 : 0;
			for (int i = 0; i < count; i++)
			{
				float angle = Mathf.Tau * i / count, radius = PreviewRadius * (.4f + .45f * ((i * 7 % 11) / 10f));
				float height = 1.4f + (i % 4) * .35f;
				var position = new Vector3(Mathf.Cos(angle) * radius, -.2f, Mathf.Sin(angle) * radius);
				AddRock(this, stoneMaterial, position, new Vector3(.5f, height, .5f), angle);
				if (i % 5 == 0) AddDust(position, .7f, 24);
			}
			if (Kind == EarthKind.Spikes) AddCracks();
		}
	}

	/// <summary>Applies the captured obstacle boundary to area surfaces, rocks and dust.</summary>
	public void SetVisibility(AreaVisibility visibility)
	{
		Visibility = visibility;
		foreach (var material in new[] { _waveMaterial, _crackMaterial, _areaStoneMaterial, _areaRingMaterial, _areaDustMaterial })
			if (material != null) visibility.Apply(material);
	}

	private void AddRock(Node parent, Material material, Vector3 position, Vector3 size, float angle)
	{
		Mesh mesh;
		if (Kind == EarthKind.Spikes) mesh = FacetedSpikeMesh.Create(size, angle, material);
		else mesh = _stoneMesh;
		var rock = new MeshInstance3D { Mesh = mesh, Position = position, Rotation = new Vector3(.12f, angle, .16f) };
		if (Kind == EarthKind.Spikes)
		{
			Vector3 outward = new Vector3(position.X, 0, position.Z).Normalized();
			rock.Basis = new Basis(Vector3.Up.Cross(outward), .3f);
		}
		if (Kind != EarthKind.Spikes) rock.Scale = size;
		parent.AddChild(rock); _rocks.Add(rock); _rockScales.Add(rock.Scale);
	}

	private void AddWave(float radius)
	{
		if (WaveShader == null) return;
		_waveMaterial = new ShaderMaterial { Shader = WaveShader };
		_wave = new MeshInstance3D { Mesh = new PlaneMesh { Size = Vector2.One * radius * 2 }, MaterialOverride = _waveMaterial, Position = Vector3.Up * .06f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = Kind != EarthKind.Rockfall };
		AddChild(_wave);
	}
	private void AddCracks()
	{
		if (CrackShader == null) return;
		_crackMaterial = new ShaderMaterial { Shader = CrackShader };
		int count = Kind == EarthKind.Spikes ? _rocks.Count + 1 : 1;
		var instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new PlaneMesh { Size = Vector2.One * 3f }, InstanceCount = count };
		for (int i = 0; i < count; i++)
		{
			Vector3 position = i == 0 ? Vector3.Zero : _rocks[i - 1].Position;
			position.Y = .09f;
			var transform = new Transform3D(new Basis(Vector3.Up, i * 1.7f), position);
			instances.SetInstanceTransform(i, transform);
			instances.SetInstanceCustomData(i, new Color(i == 0 ? 0 : new Vector2(position.X, position.Z).Length() / PreviewRadius, 0, 0));
		}
		AddChild(new MultiMeshInstance3D { Multimesh = instances, MaterialOverride = _crackMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
	}
	private void AddFragments()
	{
		_fragments = new Node3D { Name = "ImpactFragments", Visible = false };
		AddChild(_fragments);
		_fragmentMaterial = new StandardMaterial3D { AlbedoColor = new Color(.33f, .26f, .19f), Roughness = .9f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
		var fragmentMesh = (SphereMesh)_stoneMesh.Duplicate();
		fragmentMesh.Material = _fragmentMaterial;
		for (int i = 0; i < 7; i++)
			_fragments.AddChild(new MeshInstance3D { Mesh = fragmentMesh, Scale = Vector3.One * (.22f + .07f * (i % 3)) });
	}
	private void AddRing(float radius)
	{
		var mesh = new TorusMesh { InnerRadius = radius * .95f, OuterRadius = radius, Rings = 20, RingSegments = 4 };
		var ring = new MeshInstance3D { Mesh = mesh, MaterialOverride = _areaRingMaterial ?? (Material)_glow, Position = new Vector3(0, .08f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(ring); _rings.Add(ring);
	}
	private void AddDust(Vector3 position, float radius, int amount)
	{
		var fade = new Gradient { Offsets = [0, .15f, .65f, 1], Colors = [new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, .65f), new Color(1, 1, 1, 0)] };
		var particle = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = radius, Direction = Vector3.Up, Spread = 70, InitialVelocityMin = .5f, InitialVelocityMax = 2.2f, Gravity = new Vector3(0, -.6f, 0), ScaleMin = .18f, ScaleMax = .7f, ColorRamp = new GradientTexture1D { Gradient = fade } };
		var dust = new GpuParticles3D { Amount = amount, Lifetime = 1.3, OneShot = true, Explosiveness = .85f, ProcessMaterial = particle, DrawPass1 = _dustMesh, Position = position + Vector3.Up * .25f, Emitting = false };
		AddChild(dust); _dust.Add(dust); dust.Emitting = Kind == EarthKind.Tremor;
	}
	public override void _Process(double delta)
	{
		if (!Timeline.Started) return;
		if (!_started) { _started = true; Visible = true; }
		float time = Timeline.Elapsed, progress = Timeline.Progress;
		if (Kind == EarthKind.Rockfall)
		{

			if (_landed)
			{
				float impact = Mathf.Clamp((time - Fall.FlightTime) / 1.45f, 0, 1);
				_fragmentMaterial.AlbedoColor = new Color(.33f, .26f, .19f, 1 - Mathf.SmoothStep(.45f, 1, impact));
				for (int i = 0; i < _fragments.GetChildCount(); i++)
				{
					var fragment = (Node3D)_fragments.GetChild(i);
					float angle = Mathf.Tau * i / _fragments.GetChildCount();
					float distance = impact * (1.1f + .2f * (i % 3));
					fragment.Position = new Vector3(Mathf.Cos(angle) * distance, .15f + 1.2f * impact * (1 - impact), Mathf.Sin(angle) * distance);
					fragment.Rotation = new Vector3(impact * 4, angle, impact * 3);
					fragment.Scale = Vector3.One * (.22f + .07f * (i % 3)) * (1 - Mathf.SmoothStep(.45f, 1, impact));
				}
			}
			foreach (var ring in _rings) ring.Visible = _landed;
		}
		else
		{
			float erupt = Kind == EarthKind.Spikes ? Mathf.Clamp((time - .3f) / .25f, 0, 1) : Mathf.Clamp(time / .2f, 0, 1);
			if (Kind == EarthKind.Spikes && time >= .3f && !_landed) { _landed = true; StartDust(); }
			float shrink = 1 - Mathf.SmoothStep(.65f, 1, progress);
			for (int i = 0; i < _rocks.Count; i++) _rocks[i].Scale = _rockScales[i] * new Vector3(1, erupt * shrink, 1);
		}
		foreach (var ring in _rings) { float scale = Mathf.Lerp(.35f, 1, Mathf.Clamp(progress * 2, 0, 1)); ring.Scale = new Vector3(scale, 1, scale); }
		_waveMaterial?.SetShaderParameter("progress", Kind == EarthKind.Rockfall ? Mathf.Clamp((time - Fall.FlightTime) / 1.3f, 0, 1) : progress);
		float crackStart = Kind == EarthKind.Rockfall ? Fall.FlightTime : .1f;
		_crackMaterial?.SetShaderParameter("progress", Mathf.Clamp((time - crackStart) / (Timeline.Duration - crackStart), 0, 1));
		_glow.AlbedoColor = new Color(.95f, .55f, .2f, .8f * (1 - Mathf.SmoothStep(.55f, 1, progress)));
		_areaRingMaterial?.SetShaderParameter("tint", _glow.AlbedoColor);
	}
	private void OnRockImpact()
	{
		_landed = true; StartDust(); _fragments.Visible = true;
		if (_wave != null) _wave.Visible = true;
	}
	private void StartDust() { foreach (var dust in _dust) { dust.Restart(); dust.Emitting = true; } }
}
