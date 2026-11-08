using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Keeps an actor's visuals on its side of walls. Every mesh under <see cref="Target"/> (body, worn gear, held items,
/// and anything attached later) renders its authored materials through a <see cref="WallClippedMaterial"/>, and gets
/// the actor's center as its <c>wall_clip_anchor</c> instance parameter, refreshed while the actor moves
/// (<c>wall_clip.gdshaderinc</c>). The anchor stays on the mesh whatever material an effect swaps in, so effects keep
/// clipping. Visual helpers (auras, x-rays, effect layers) keep their own materials. Meant for the player and bosses
/// whose reach crosses walls; instance <c>wall_clip_component.tscn</c>.
/// </summary>
[GlobalClass]
public partial class WallClipComponent : Node
{
	private static readonly StringName AnchorParameter = "wall_clip_anchor";

	/// <summary>The actor's visual root. Its position is where paths to walls start, so it must stay clear of walls.</summary>
	[Export] public Node3D Target { get; set; }
	/// <summary><c>wall_clipped.gdshader</c>. Unassigned disables clipping and keeps the authored materials.</summary>
	[Export] public Shader Shader { get; set; }
	/// <summary><c>wall_clipped_alpha.gdshader</c>, for sources using depth-prepass alpha and for death fades.</summary>
	[Export] public Shader AlphaShader { get; set; }

	/// <summary>One clipped material per authored source, so meshes sharing a material still share one; null for sources
	/// the shader cannot reproduce.</summary>
	private readonly Dictionary<BaseMaterial3D, WallClippedMaterial> _materials = new();
	private readonly List<MeshInstance3D> _clipped = new();
	private Action _unwatch;
	private Vector2 _anchor = Vector2.Inf;

	private Vector2 Anchor => new(Target.GlobalPosition.X, Target.GlobalPosition.Z);

	/// <summary>Clips before any sibling's <c>_Ready</c>, so effects started there capture the clipped materials.</summary>
	public override void _EnterTree()
	{
		if (_unwatch != null || Engine.IsEditorHint() || Target == null || Shader == null) return;
		Apply(Target);
		_unwatch = this.SubscribeUntilExit(GetTree(), tree => tree.NodeAdded += OnNodeAdded, tree => tree.NodeAdded -= OnNodeAdded);
	}

	public override void _Ready() => SetProcess(_unwatch != null);

	public override void _Process(double delta)
	{
		Vector2 anchor = Anchor;
		if (anchor.DistanceSquaredTo(_anchor) < 0.0001f) return;
		_anchor = anchor;
		_clipped.RemoveAll(mesh => !IsInstanceValid(mesh));
		foreach (var mesh in _clipped) mesh.SetInstanceShaderParameter(AnchorParameter, anchor);
	}

	/// <summary>Gear and models clip from the moment they enter the tree, before a running surface effect captures them.</summary>
	private void OnNodeAdded(Node node)
	{
		if (node is MeshInstance3D mesh && Target.IsAncestorOf(mesh) && !VisualHelper.IsInHelperSubtree(mesh)) Clip(mesh);
	}

	private void Apply(Node node)
	{
		if (VisualHelper.IsHelper(node)) return;
		if (node is MeshInstance3D mesh) Clip(mesh);
		foreach (Node child in node.GetChildren()) Apply(child);
	}

	private void Clip(MeshInstance3D mesh)
	{
		if (mesh.Mesh == null) return;
		bool clipped = false;
		if (mesh.MaterialOverride is BaseMaterial3D whole)
		{
			if (ClippedFor(whole) is { } material)
			{
				mesh.MaterialOverride = material;
				clipped = true;
			}
		}
		else
		{
			for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				if (mesh.GetActiveMaterial(i) is BaseMaterial3D source && ClippedFor(source) is { } material)
				{
					mesh.SetSurfaceOverrideMaterial(i, material);
					clipped = true;
				}
		}
		if (!clipped || _clipped.Contains(mesh)) return;
		_clipped.Add(mesh);
		mesh.SetInstanceShaderParameter(AnchorParameter, Anchor);
	}

	private WallClippedMaterial ClippedFor(BaseMaterial3D source)
	{
		if (!_materials.TryGetValue(source, out var clipped))
			_materials[source] = clipped = WallClippedMaterial.Create(source, Shader, AlphaShader);
		return clipped;
	}
}