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
	private sealed record FlashSurface(MeshInstance3D Mesh, int Surface, Material OriginalOverride,
		Material Material, StringName ColorProperty, Color OriginalColor);
	private readonly List<FlashSurface> _flashSurfaces = [];
	private Color _flashColor;
	private float _flashAmount;
	private float _flashRemaining;
	private float _flashDuration;

	public override void _Process(double delta)
	{
		if (_flashRemaining <= 0) return;
		_flashRemaining = Mathf.Max(0, _flashRemaining - (float)delta);
		float amount = _flashAmount * (_flashDuration <= 0 ? 0 : _flashRemaining / _flashDuration);
		if (_winner?.Profile.Kind == SurfaceAppearanceKind.Skin)
		{
			foreach (var surface in _meshes.Values)
				foreach (var material in surface.Materials)
				{
					material.SetShaderParameter("hit_flash_color", _flashColor);
					material.SetShaderParameter("hit_flash_amount", amount);
				}
		}
		else
		{
			foreach (var surface in _flashSurfaces)
				if (IsInstanceValid(surface.Mesh) && surface.Surface < surface.Mesh.Mesh.GetSurfaceCount())
					surface.Material.Set(surface.ColorProperty, surface.OriginalColor.Lerp(_flashColor, amount));
		}
		if (_flashRemaining <= 0) ClearFlash();
	}

	/// <summary>Pulse a private source-preserving material channel; cloak and teleport transitions suppress it.</summary>
	public bool PulseHit(Color color, float amount, float duration)
	{
		if (IsStopped || Target == null || HasCloak || _winner?.Profile.Kind == SurfaceAppearanceKind.Transition
			|| amount <= 0 || duration <= 0) return false;
		ClearFlash();
		_flashColor = color;
		_flashAmount = Mathf.Clamp(amount, 0, 1);
		_flashDuration = duration;
		_flashRemaining = duration;
		if (_winner?.Profile.Kind == SurfaceAppearanceKind.Skin) return true;
		CaptureFlash(Target);
		return _flashSurfaces.Count > 0;
	}

	/// <summary>Reject unsupported materials before modifying the subtree. Existing tokens update in place.</summary>
	public bool Acquire(object token, StringName effectId, SurfaceAppearanceProfile profile, float amount)
	{
		if (IsStopped || token == null || Target == null || profile?.Material?.Shader == null || !IsInsideTree()) return false;
		ClearFlash();
		if (_requests.Count == 0 && !SurfaceMaterialFactory.CanCapture(Target)) return false;
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
		ClearFlash();
		IsStopped |= terminal; _requests.Clear(); _winner = null;
		_unwatch?.Invoke(); _unwatch = null;
		foreach (var surface in _meshes.Values)
			if (surface.Snapshot.RestoreMaterials()) surface.Snapshot.Mesh.CastShadow = surface.Snapshot.CastShadow;
		_meshes.Clear(); EmitSignalStateChanged();
	}
	public bool CanApply => !IsStopped && Target != null && (_requests.Count > 0 || SurfaceMaterialFactory.CanCapture(Target));
	private void Refresh()
	{
		Request winner = null;
		foreach (var request in _requests.Values)
			if (SurfaceMaterialFactory.IsHigherPriority(request.Profile, request.Id, winner?.Profile, winner?.Id)) winner = request;
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
			if (!SurfaceMaterialFactory.CanCapture(mesh)) return;
			_meshes.Add(mesh, Build(new MeshMaterialSnapshot(mesh)));
		}
		foreach (Node child in node.GetChildren()) Capture(child);
	}
	private Surface Build(MeshMaterialSnapshot snapshot)
	{
		var (overrides, materials) = SurfaceMaterialFactory.Create(snapshot, _winner.Profile, _winner.Amount);
		snapshot.ApplySurfaces(overrides, clearOverlay: true);
		snapshot.Mesh.CastShadow = _winner.Profile.Kind == SurfaceAppearanceKind.Cloak ? GeometryInstance3D.ShadowCastingSetting.Off : snapshot.CastShadow;
		return new Surface(snapshot, materials);
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

	private void CaptureFlash(Node node)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D { Mesh: not null } mesh)
		{
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
			{
				var active = mesh.GetActiveMaterial(i);
				if (WallClippedMaterial.PrivateCopy(active, blending: false) is not var (copy, color)) continue;
				Color original = WallClippedMaterial.SourceOf(active).AlbedoColor;
				_flashSurfaces.Add(new FlashSurface(mesh, i, mesh.GetSurfaceOverrideMaterial(i), copy, color, original));
				mesh.SetSurfaceOverrideMaterial(i, copy);
				copy.Set(color, original.Lerp(_flashColor, _flashAmount));
			}
		}
		foreach (Node child in node.GetChildren()) CaptureFlash(child);
	}

	private void ClearFlash()
	{
		foreach (var surface in _flashSurfaces)
			if (IsInstanceValid(surface.Mesh)) surface.Mesh.SetSurfaceOverrideMaterial(surface.Surface, surface.OriginalOverride);
		_flashSurfaces.Clear();
		foreach (var surface in _meshes.Values)
			foreach (var material in surface.Materials)
			{
				material.SetShaderParameter("hit_flash_amount", 0f);
				material.SetShaderParameter("hit_flash_color", Colors.White);
			}
		_flashRemaining = 0;
	}
	public override void _ExitTree() => ClearAndRestore();
}
