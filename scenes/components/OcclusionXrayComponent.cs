using Godot;
using System.Collections.Generic;

/// <summary>
/// Which global debug toggle governs an <see cref="OcclusionXrayComponent"/>.
/// </summary>
public enum XrayCategory
{
	Monster,
	Loot,
}

/// <summary>
/// Draws see-through-wall silhouettes for an actor's meshes: for every <see cref="MeshInstance3D"/>
/// under <see cref="TargetRoot"/> it builds two drawn-on-top duplicates — a stencil "mask" and the
/// visible x-ray. The mask marks the pixels where the actor is actually visible; the x-ray then
/// draws only where the actor is hidden (stencil != 1). Because the gate is per-pixel rather than
/// per-fragment, the actor never x-rays through its own body, regardless of how deep or complex
/// it is (see the two <c>occlusion_xray*.gdshader</c> files).
///
/// Silhouettes are shown only when the category's global toggle is on (<see cref="GameDebug"/>)
/// AND the actor is in a discovered room (<see cref="MapGenerator.IsRevealedAt"/>), matching how
/// the door indicator is gated to revealed areas.
/// </summary>
[GlobalClass]
public partial class OcclusionXrayComponent : Node
{
	private const string MaskShaderPath = "res://scenes/effects/shaders/occlusion_xray_mask.gdshader";
	private const string XrayShaderPath = "res://scenes/effects/shaders/occlusion_xray.gdshader";
	// Transparent draw order: every mask must render (and write its stencil) before any x-ray reads it.
	private const int MaskRenderPriority = 0;
	private const int XrayRenderPriority = 8;
	private const double PollInterval = 0.3;

	/// <summary>Root whose <see cref="MeshInstance3D"/> descendants get silhouettes.</summary>
	[Export] public NodePath TargetRoot { get; set; }
	/// <summary>Silhouette tint (alpha is honored; the shader fades it further with distance).</summary>
	[Export] public Color XrayColor { get; set; } = new(1f, 0.35f, 0.35f, 0.85f);
	/// <summary>Which debug toggle enables this component.</summary>
	[Export] public XrayCategory Category { get; set; } = XrayCategory.Monster;

	private static Shader _maskShader;
	private static Shader _xrayShader;
	private static Shader MaskShader => _maskShader ??= GD.Load<Shader>(MaskShaderPath);
	private static Shader XrayShader => _xrayShader ??= GD.Load<Shader>(XrayShaderPath);

	private readonly List<MeshInstance3D> _silhouettes = new();
	private Node3D _targetRoot;
	private MapGenerator _mapGenerator;
	private HealthComponent _health;
	private double _pollAccumulator;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		_targetRoot = GetNodeOrNull<Node3D>(TargetRoot);
		if (_targetRoot == null)
		{
			GD.PrintErr($"{Name}: OcclusionXrayComponent has no valid TargetRoot.");
			return;
		}

		_mapGenerator = this.GetAncestorOrNull<Level>()?.MapGenerator;
		_health = GetParent()?.GetNodeOrNull<HealthComponent>("HealthComponent");

		BuildSilhouettes(_targetRoot);

		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.RoomEntered += OnRoomEntered,
			bus => bus.RoomEntered -= OnRoomEntered);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			bus => bus.XraySettingsChanged += OnXraySettingsChanged,
			bus => bus.XraySettingsChanged -= OnXraySettingsChanged);

		Evaluate();
	}

	public override void _Process(double delta)
	{
		// Actors move, so re-check "is in a discovered room" on a low-frequency poll.
		// Loot is static and would be covered by RoomEntered alone, but polling keeps
		// one code path and the cost is a single dictionary lookup.
		if (_silhouettes.Count == 0)
		{
			return;
		}

		_pollAccumulator += delta;
		if (_pollAccumulator >= PollInterval)
		{
			_pollAccumulator = 0;
			Evaluate();
		}
	}

	/// <summary>
	/// Creates the mask + x-ray duplicate pair for every mesh under <paramref name="node"/>. Each
	/// duplicate is parented next to its source and inherits the source's local transform, skin,
	/// and skeleton binding, so skinned enemy meshes deform with their animation.
	/// </summary>
	private void BuildSilhouettes(Node node)
	{
		if (node is MeshInstance3D src && src.Mesh != null)
		{
			AddDuplicate(src, CreateMaskMaterial());
			AddDuplicate(src, CreateXrayMaterial());
		}

		foreach (Node child in node.GetChildren())
		{
			BuildSilhouettes(child);
		}
	}

	private void AddDuplicate(MeshInstance3D src, ShaderMaterial material)
	{
		var duplicate = new MeshInstance3D
		{
			Name = $"{src.Name}_Xray",
			Mesh = src.Mesh,
			Skin = src.Skin,
			Skeleton = src.Skeleton,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Transform = src.Transform,
			Visible = false,
		};
		src.GetParent().AddChild(duplicate);
		_silhouettes.Add(duplicate);
	}

	private ShaderMaterial CreateMaskMaterial() => new()
	{
		Shader = MaskShader,
		RenderPriority = MaskRenderPriority,
	};

	private ShaderMaterial CreateXrayMaterial()
	{
		var material = new ShaderMaterial
		{
			Shader = XrayShader,
			RenderPriority = XrayRenderPriority,
		};
		material.SetShaderParameter("xray_color", XrayColor);
		return material;
	}

	private bool GlobalFlagEnabled() => Category switch
	{
		XrayCategory.Loot => GameDebug.LootXrayEnabled,
		_ => GameDebug.MonsterXrayEnabled,
	};

	/// <summary>Recomputes and applies silhouette visibility from the toggle + reveal + alive state.</summary>
	private void Evaluate()
	{
		if (_silhouettes.Count == 0 || _targetRoot == null)
		{
			return;
		}

		bool revealed = _mapGenerator == null || _mapGenerator.IsRevealedAt(_targetRoot.GlobalPosition);
		bool alive = _health == null || !_health.IsDead;
		bool visible = GlobalFlagEnabled() && revealed && alive;

		foreach (MeshInstance3D silhouette in _silhouettes)
		{
			silhouette.Visible = visible;
		}
	}

	private void OnRoomEntered(int roomId) => Evaluate();

	private void OnXraySettingsChanged() => Evaluate();
}
