using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Single reversible material owner for cloak and skins, including gear attached during an effect.</summary>
[GlobalClass]
public partial class SurfaceAppearanceController : Node
{
	[Signal] public delegate void StateChangedEventHandler();
	[Export] public Node3D Target { get; set; }
	public bool HasCloak => _requests.Values.Any(r => r.Profile.Kind == SurfaceAppearanceKind.Cloak);
	public float CloakAmount => _requests.Values.Where(r => r.Profile.Kind == SurfaceAppearanceKind.Cloak).Select(r => r.Amount).DefaultIfEmpty().Max();
	public bool IsStopped { get; private set; }
	private sealed class Request(StringName id, SurfaceAppearanceProfile profile, float amount)
	{
		public StringName Id = id;
		public SurfaceAppearanceProfile Profile = profile;
		public float Amount = amount;
	}
	private sealed record Surface(MeshMaterialSnapshot Snapshot, ShaderMaterial[] Materials);
	private readonly Dictionary<object, Request> _requests = [];
	private readonly Dictionary<MeshInstance3D, Surface> _meshes = [];
	private Request _winner;
	private Action _unwatch;

	/// <summary>Reject unsupported materials before modifying the subtree. Existing tokens update in place.</summary>
	public bool Acquire(object token, StringName effectId, SurfaceAppearanceProfile profile, float amount)
	{
		if (IsStopped || token == null || Target == null || profile?.Material?.Shader == null || !IsInsideTree()) return false;
		if (_requests.Count == 0 && !CanCapture(Target)) return false;
		if (_requests.TryGetValue(token, out var request))
		{ request.Id = effectId; request.Profile = profile; request.Amount = Mathf.Clamp(amount, 0, 1); }
		else _requests.Add(token, new Request(effectId, profile, Mathf.Clamp(amount, 0, 1)));
		_unwatch ??= this.SubscribeUntilExit(GetTree(), t => t.NodeAdded += OnAdded, t => t.NodeAdded -= OnAdded);
		Refresh(); return true;
	}
	public void SetAmount(object token, float amount)
	{
		if (!_requests.TryGetValue(token, out var request)) return;
		amount = Mathf.Clamp(amount, 0, 1);
		if (request.Amount == amount) return;
		request.Amount = amount;
		if (request == _winner) ApplyAmount();
	}
	public void Release(object token)
	{
		if (token != null && _requests.Remove(token)) Refresh();
	}
	/// <summary>Restore authored slots. Terminal shutdown prevents death fades being captured by later cues.</summary>
	public void ClearAndRestore(bool terminal = false)
	{
		IsStopped |= terminal; _requests.Clear(); _winner = null;
		_unwatch?.Invoke(); _unwatch = null;
		foreach (var surface in _meshes.Values)
			if (surface.Snapshot.RestoreMaterials()) surface.Snapshot.Mesh.CastShadow = surface.Snapshot.CastShadow;
		_meshes.Clear(); EmitSignalStateChanged();
	}
	public bool CanApply => !IsStopped && Target != null && (_requests.Count > 0 || CanCapture(Target));
	private bool CanCapture(Node node)
	{
		if (VisualHelper.IsHelper(node)) return true;
		if (node is MeshInstance3D { Mesh: not null } mesh)
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				if (mesh.GetActiveMaterial(i) is not null and not BaseMaterial3D)
				{ GD.PushWarning($"{Name}: unsupported source shader on {mesh.Name}; assign a compatible visual variant."); return false; }
		foreach (Node child in node.GetChildren()) if (!CanCapture(child)) return false;
		return true;
	}
	private static int Rank(SurfaceAppearanceKind kind) => kind switch { SurfaceAppearanceKind.Cloak => 2, SurfaceAppearanceKind.Transition => 1, _ => 0 };
	private void Refresh()
	{
		Request winner = null;
		foreach (var request in _requests.Values)
			if (winner == null || Rank(request.Profile.Kind) > Rank(winner.Profile.Kind)
				|| (Rank(request.Profile.Kind) == Rank(winner.Profile.Kind) && (request.Profile.Priority > winner.Profile.Priority
				|| (request.Profile.Priority == winner.Profile.Priority && string.CompareOrdinal(request.Id.ToString(), winner.Id.ToString()) < 0)))) winner = request;
		if (winner == null) { ClearAndRestore(); return; }
		if (_winner != winner)
		{
			// Restore the one baseline before making the next material; never snapshot another effect.
			foreach (var surface in _meshes.Values) surface.Snapshot.RestoreMaterials();
			_winner = winner;
			foreach (var mesh in _meshes.Keys.ToArray())
				if (IsInstanceValid(mesh)) _meshes[mesh] = Build(_meshes[mesh].Snapshot); else _meshes.Remove(mesh);
		}
		Capture(Target); ApplyAmount(); EmitSignalStateChanged();
	}
	private void Capture(Node node)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh && !_meshes.ContainsKey(mesh))
		{
			if (!CanCapture(mesh)) return;
			_meshes.Add(mesh, Build(new MeshMaterialSnapshot(mesh)));
		}
		foreach (Node child in node.GetChildren()) Capture(child);
	}
	private Surface Build(MeshMaterialSnapshot snapshot)
	{
		var overrides = new ShaderMaterial[snapshot.SurfaceCount];
		var materials = new List<ShaderMaterial>();
		for (int i = 0; i < overrides.Length; i++)
		{
			ShaderMaterial previous = null;
			for (var template = _winner.Profile.Material; template != null; template = template.NextPass as ShaderMaterial)
			{
				var material = (ShaderMaterial)template.Duplicate(); material.NextPass = null;
				if (previous == null) overrides[i] = material; else previous.NextPass = material;
				previous = material;
				if (snapshot.ActiveMaterial(i) is BaseMaterial3D source)
				{
					material.SetShaderParameter("base_color", source.AlbedoColor);
					material.SetShaderParameter("source_texture", source.AlbedoTexture);
					material.SetShaderParameter("source_roughness", source.Roughness);
					material.SetShaderParameter("source_metallic", source.Metallic);
					material.SetShaderParameter("source_specular", source.MetallicSpecular);
					material.SetShaderParameter("source_uv_scale", source.Uv1Scale);
					material.SetShaderParameter("source_uv_offset", source.Uv1Offset);
					material.SetShaderParameter("source_normal", source.NormalTexture);
					material.SetShaderParameter("source_normal_enabled", source.NormalEnabled);
					material.SetShaderParameter("source_normal_scale", source.NormalScale);
					material.SetShaderParameter("source_alpha_scissor", source.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor ? source.AlphaScissorThreshold : 0f);
					material.SetShaderParameter("source_cull", (int)source.CullMode);
				}
				material.SetShaderParameter("amount", _winner.Amount); materials.Add(material);
			}
		}
		snapshot.ApplySurfaces(overrides, clearOverlay: true);
		snapshot.Mesh.CastShadow = _winner.Profile.Kind == SurfaceAppearanceKind.Cloak ? GeometryInstance3D.ShadowCastingSetting.Off : snapshot.CastShadow;
		return new Surface(snapshot, materials.ToArray());
	}
	private void ApplyAmount()
	{
		foreach (var (mesh, surface) in _meshes)
			if (IsInstanceValid(mesh)) foreach (var material in surface.Materials) material.SetShaderParameter("amount", _winner.Amount);
	}
	private void OnAdded(Node node)
	{
		if (node is not MeshInstance3D || !Target.IsAncestorOf(node)) return;
		Callable.From(() =>
		{
			if (IsInsideTree() && _winner != null && IsInstanceValid(node) && Target.IsAncestorOf(node) && !VisualHelper.IsInHelperSubtree(node)) Capture(node);
		}).CallDeferred();
	}
	public override void _ExitTree() => ClearAndRestore();
}