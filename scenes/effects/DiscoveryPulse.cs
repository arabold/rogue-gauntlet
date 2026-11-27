using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Temporary surface glow and crossfade. Physical collision and navigation remain owned by the prop. Standard materials
/// fade through their albedo and glow through their emission; a shader material takes part when its shader declares
/// <c>uniform float reveal</c> (its opacity) and <c>uniform vec3 reveal_glow</c> (added light).
/// </summary>
[Tool]
public partial class DiscoveryPulse : Node3D
{
	[Export(PropertyHint.Range, "0.2,4")] public float Duration { get; set; } = 0.6f;
	[Export(PropertyHint.Range, "0,4")] public float GlowStrength { get; set; } = 1.8f;
	[Export] public Color GlowColor { get; set; } = new(1, 0.78f, 0.08f);
	[Export] public bool Blend { get; set; } = true;
	public bool IsPlaying { get; private set; }
	private static readonly StringName RevealParameter = "reveal", RevealGlowParameter = "reveal_glow";
	// Material alpha works on Mobile too; GeometryInstance3D.Transparency only works on Forward+.
	private sealed class Surface
	{
		public MeshMaterialSnapshot State;
		public BaseMaterial3D[] Fades;
		public ShaderMaterial[] Reveals;
		public Color[] Colors, Emission;
		public BaseMaterial3D.TransparencyEnum[] Transparency;
		public bool Original;
	}
	private readonly List<Surface> _targets = [];
	private MeshInstance3D _original;
	private Tween _tween;
	public override void _Ready() => Visible = false;
	/// <summary>Capture a visual-only copy of the actual selected tile, with its material and orientation.</summary>
	public MeshInstance3D CaptureCell(GridMap grid, Vector3I cell, float lift = 0)
	{
		int item = grid.GetCellItem(cell);
		if (item < 0) return null;
		var visual = new MeshInstance3D { Mesh = grid.MeshLibrary.GetItemMesh(item), Name = "ConcealedSurface" };
		AddChild(visual);
		visual.GlobalTransform = grid.GlobalTransform * grid.GetCellTransform(cell) * grid.MeshLibrary.GetItemMeshTransform(item);
		visual.GlobalPosition += Vector3.Up * lift;
		return visual;
	}
	public void Play(Node3D target, MeshInstance3D original = null)
	{
		Stop();
		if (Engine.IsEditorHint() || target == null) { original?.QueueFree(); return; }
		_original = original;
		var meshes = target.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToList();
		if (target is MeshInstance3D root) meshes.Insert(0, root);
		foreach (var mesh in meshes.Where(m => !VisualHelper.IsHelper(m))) AddSurface(mesh, false);
		if (_original != null) AddSurface(_original, true);
		IsPlaying = Visible = true;
		ApplyProgress(0);
		_tween = CreateTween();
		_tween.TweenMethod(Callable.From<float>(ApplyProgress), 0f, 1f, Mathf.Max(0.2f, Duration));
		_tween.Finished += Stop;
	}
	private void AddSurface(MeshInstance3D mesh, bool original)
	{
		var state = new MeshMaterialSnapshot(mesh);
		int count = state.SurfaceCount;
		var surface = new Surface { State = state, Original = original, Fades = new BaseMaterial3D[count], Colors = new Color[count],
			Emission = new Color[count], Transparency = new BaseMaterial3D.TransparencyEnum[count], Reveals = new ShaderMaterial[count] };
		var shown = new Material[count];
		for (int i = 0; i < count; i++)
		{
			shown[i] = state.ActiveMaterial(i);
			// Production discovery meshes use StandardMaterial3D. Never modify their shared definitions.
			if (shown[i] is BaseMaterial3D material)
			{
				var fade = (BaseMaterial3D)material.Duplicate();
				surface.Colors[i] = fade.AlbedoColor;
				surface.Emission[i] = fade.EmissionEnabled ? fade.Emission * fade.EmissionEnergyMultiplier : Colors.Black;
				surface.Transparency[i] = fade.Transparency;
				fade.EmissionEnabled = true;
				fade.EmissionEnergyMultiplier = 1;
				surface.Fades[i] = fade;
				shown[i] = fade;
			}
			else if (shown[i] is ShaderMaterial { Shader: { } shader } custom && Reveals(shader))
			{
				var reveal = (ShaderMaterial)custom.Duplicate();
				surface.Reveals[i] = reveal;
				shown[i] = reveal;
			}
		}
		// One material owns both glow and opacity throughout the transition. An overlay would
		// change depth behavior when the base enters/leaves alpha rendering, causing flashes.
		state.ApplySurfaces(shown);
		_targets.Add(surface);
	}
	private void ApplyProgress(float progress)
	{
		// Hold the original surface briefly while it warms up, then blend and cool down.
		float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp((progress - 0.2f) / 0.6f, 0, 1));
		foreach (var surface in _targets)
		{
			if (!IsInstanceValid(surface.State.Mesh)) continue;
			float opacity = surface.Original ? (Blend ? 1 - blend : 0) : (Blend ? blend : 1);
			surface.State.Mesh.Visible = surface.State.Visible && opacity > 0.001f;
			float glow = Mathf.Max(0, Mathf.Sin(progress * Mathf.Pi)) * GlowStrength;
			for (int i = 0; i < surface.Reveals.Length; i++)
			{
				if (surface.Reveals[i] == null) continue;
				surface.Reveals[i].SetShaderParameter(RevealParameter, opacity);
				surface.Reveals[i].SetShaderParameter(RevealGlowParameter, new Vector3(GlowColor.R, GlowColor.G, GlowColor.B) * glow);
			}
			for (int i = 0; i < surface.Fades.Length; i++)
				if (surface.Fades[i] != null)
				{
					var material = surface.Fades[i];
					var color = surface.Colors[i]; color.A *= opacity;
					material.AlbedoColor = color;
					// Keep opaque depth occlusion through warmup and restore it once the fade is complete.
					material.Transparency = opacity < 0.99999f ? BaseMaterial3D.TransparencyEnum.Alpha : surface.Transparency[i];
					material.Emission = surface.Emission[i] + GlowColor * glow;
				}
		}
	}
	/// <summary>Whether <paramref name="shader"/> takes part in the fade through a <c>reveal</c> uniform.</summary>
	private static bool Reveals(Shader shader) =>
		shader.GetShaderUniformList().Any(uniform => uniform.AsGodotDictionary()["name"].AsString() == RevealParameter.ToString());

	public void Stop()
	{
		_tween?.Kill(); _tween = null;
		foreach (var surface in _targets)
			if (surface.State.RestoreMaterials()) surface.State.Mesh.Visible = surface.State.Visible;
		_targets.Clear();
		if (_original != null && IsInstanceValid(_original)) _original.QueueFree();
		_original = null;
		IsPlaying = Visible = false;
	}
	public override void _ExitTree() => Stop();
}