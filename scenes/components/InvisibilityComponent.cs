using Godot;
using System.Collections.Generic;

/// <summary>Shared actor cloak: faint animated surfaces, equipment and shadow suppression, with reversible materials.
/// This is presentation only; perception, collision and combat remain the actor's responsibility.</summary>
[GlobalClass]
public partial class InvisibilityComponent : Node
{
	[Signal] public delegate void ChangedEventHandler();
	[Export] public Node3D Target { get; set; }
	[Export] public ShaderMaterial EffectMaterial { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public bool StartInvisible { get; set; }
	[Export(PropertyHint.Range, "0,2,0.05")] public float FadeDuration { get; set; } = 0.25f;
	public bool IsInvisible => _requested || _amount > 0;
	public float Amount => _amount;
	private sealed record Surface(MeshMaterialSnapshot State, ShaderMaterial[] Cloak);
	private readonly Dictionary<MeshInstance3D, Surface> _meshes = [];
	private Tween _tween;
	private System.Action _stopWatchingEquipment;
	private bool _requested;
	private float _amount;
	public override void _Ready()
	{
		if (Target == null || EffectMaterial?.Shader == null)
		{ GD.PushError($"{Name}: invisibility requires an assigned Target and EffectMaterial shader."); return; }
		if (Health != null) this.SubscribeUntilExit(Health, h => h.Died += OnDied, h => h.Died -= OnDied);
		if (StartInvisible) SetInvisible(true, true);
	}
	/// <summary>Fade into or out of invisibility. Repeated requests are idempotent; reversals continue from the current amount.</summary>
	public void SetInvisible(bool invisible, bool immediate = false)
	{
		if (Target == null || EffectMaterial?.Shader == null || !IsInsideTree()) return;
		if (!immediate && _requested == invisible) return;
		_tween?.Kill(); _tween = null; _requested = invisible;
		if (invisible)
		{
			Capture(Target);
			// Godot has no subtree-added signal, so watch the tree only while cloaked to catch equipment attached later.
			_stopWatchingEquipment ??= this.SubscribeUntilExit(GetTree(), t => t.NodeAdded += OnNodeAdded, t => t.NodeAdded -= OnNodeAdded);
		}
		EmitSignal(SignalName.Changed);
		float end = invisible ? 1 : 0;
		if (immediate || FadeDuration <= 0) { ApplyAmount(end); Finish(); return; }
		_tween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_tween.TweenMethod(Callable.From<float>(ApplyAmount), _amount, end, FadeDuration);
		_tween.Finished += Finish;
	}
	private void Capture(Node node)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh && !_meshes.ContainsKey(mesh))
		{
			var snapshot = new MeshMaterialSnapshot(mesh);
			var cloaks = new ShaderMaterial[snapshot.SurfaceCount];
			for (int i = 0; i < cloaks.Length; i++)
			{
				var cloak = (ShaderMaterial)EffectMaterial.Duplicate();
				if (snapshot.ActiveMaterial(i) is BaseMaterial3D source)
				{
					cloak.SetShaderParameter("base_color", source.AlbedoColor);
					if (source.AlbedoTexture != null) cloak.SetShaderParameter("source_texture", source.AlbedoTexture);
					cloak.SetShaderParameter("source_roughness", source.Roughness);
					cloak.SetShaderParameter("source_metallic", source.Metallic);
				}
				cloak.SetShaderParameter("amount", _amount); cloaks[i] = cloak;
			}
			snapshot.ApplySurfaces(cloaks, clearOverlay: true);
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			_meshes.Add(mesh, new Surface(snapshot, cloaks));
		}
		foreach (Node child in node.GetChildren()) Capture(child);
	}
	private void ApplyAmount(float amount)
	{
		_amount = Mathf.Clamp(amount, 0, 1);
		foreach (var (mesh, state) in _meshes)
			if (IsInstanceValid(mesh)) foreach (var material in state.Cloak) material.SetShaderParameter("amount", _amount);
	}
	private void Finish()
	{
		_tween = null;
		if (!_requested) Restore();
		EmitSignal(SignalName.Changed);
	}
	private void OnNodeAdded(Node node)
	{
		// Equipment is attached at runtime. Defer until its subtree has finished entering the tree.
		if (node is not MeshInstance3D) return;
		Callable.From(() =>
		{
			if (IsInsideTree() && IsInvisible && IsInstanceValid(node) && Target.IsAncestorOf(node)) Capture(node);
		}).CallDeferred();
	}
	private void Restore()
	{
		_stopWatchingEquipment?.Invoke(); _stopWatchingEquipment = null;
		foreach (var (mesh, surface) in _meshes)
			if (surface.State.RestoreMaterials()) mesh.CastShadow = surface.State.CastShadow;
		_meshes.Clear(); _amount = 0;
	}
	private void OnDied() => SetInvisible(false, true); // Restore before the ordinary corpse fade takes ownership.
	public override void _ExitTree() { _tween?.Kill(); _tween = null; _requested = false; Restore(); }
}