using System.Collections.Generic;
using Godot;

/// <summary>
/// Reusable fade-out for 3D content: smoothly tweens every visible surface under a target
/// to fully transparent, then optionally frees the target. Mesh surfaces fade via their
/// material alpha, while sprites and labels (e.g. floating health bars) fade via modulate
/// alpha, so the whole actor eases away together. Use it as a drop-in component (set
/// <see cref="Target"/> or rely on the owner, then call <see cref="Begin"/>) or call the
/// static <see cref="FadeOutAndFree"/> directly from code (e.g. when an enemy dies or a
/// prop despawns).
///
/// Mesh fading replaces each surface's active base or wall-clipped material with an instance-local
/// blending copy (<see cref="WallClippedMaterial.PrivateCopy"/>) and tweens its color alpha to 0.
/// Copying keeps shared authored materials untouched (so fading one enemy never fades the rest),
/// and alpha blending is supported on every renderer — unlike the per-instance
/// <c>GeometryInstance3D.transparency</c> property, which only works on Forward+.
/// </summary>
[GlobalClass]
public partial class FadeOutComponent : Node
{
	[Signal]
	public delegate void FinishedEventHandler();

	/// <summary>Subtree to fade. Defaults to this component's scene owner when left unset.</summary>
	[Export] public Node3D Target { get; set; }

	[Export] public float Duration { get; set; } = 0.5f;
	[Export] public Tween.TransitionType Trans { get; set; } = Tween.TransitionType.Sine;
	[Export] public Tween.EaseType Ease { get; set; } = Tween.EaseType.Out;

	/// <summary>Frees the faded target once the fade completes.</summary>
	[Export] public bool FreeTargetOnFinish { get; set; } = true;

	/// <summary>
	/// Starts fading the configured <see cref="Target"/> (or the scene owner) and emits
	/// <see cref="Finished"/> when done.
	/// </summary>
	public void Begin()
	{
		var target = Target ?? GetOwnerOrNull<Node3D>();
		if (target == null)
		{
			GD.PushWarning($"{nameof(FadeOutComponent)} on '{Name}' has no target to fade.");
			EmitSignal(SignalName.Finished);
			return;
		}

		var tween = FadeOutAndFree(target, Duration, FreeTargetOnFinish, Trans, Ease);
		if (tween == null)
		{
			EmitSignal(SignalName.Finished);
			return;
		}

		tween.Finished += () => EmitSignal(SignalName.Finished);
	}

	/// <summary>
	/// Fades every visual surface under <paramref name="target"/> to fully transparent over
	/// <paramref name="duration"/> seconds, then frees the target when <paramref name="freeOnFinish"/>
	/// is set. Returns the driving tween (so callers can await its <c>Finished</c> signal), or
	/// <c>null</c> when the target has no fadeable surfaces.
	/// </summary>
	public static Tween FadeOutAndFree(Node3D target, float duration, bool freeOnFinish = true,
		Tween.TransitionType trans = Tween.TransitionType.Sine,
		Tween.EaseType ease = Tween.EaseType.Out)
	{
		var fadeTargets = new List<(GodotObject Object, string Property)>();
		CollectFadeTargets(target, fadeTargets);

		if (fadeTargets.Count == 0)
		{
			if (freeOnFinish)
			{
				target.QueueFree();
			}

			return null;
		}

		var tween = target.CreateTween();
		tween.SetParallel();
		tween.SetTrans(trans);
		tween.SetEase(ease);

		foreach (var (fadeObject, property) in fadeTargets)
		{
			tween.TweenProperty(fadeObject, property, 0.0f, duration);
		}

		if (freeOnFinish)
		{
			tween.Finished += target.QueueFree;
		}

		return tween;
	}

	/// <summary>
	/// Gathers the alpha properties to fade under <paramref name="node"/>: mesh surfaces (via
	/// instance-local alpha-blended material duplicates) and sprite/label nodes (via their
	/// modulate alpha, so floating health bars and labels fade with the body instead of popping).
	/// </summary>
	private static void CollectFadeTargets(Node node, List<(GodotObject Object, string Property)> result)
	{
		switch (node)
		{
			case MeshInstance3D { Mesh: not null } meshInstance:
				CollectMeshFadeMaterials(meshInstance, result);
				break;
			case SpriteBase3D sprite:
				result.Add((sprite, "modulate:a"));
				break;
			case Label3D label:
				result.Add((label, "modulate:a"));
				break;
		}

		foreach (var child in node.GetChildren())
		{
			CollectFadeTargets(child, result);
		}
	}

	/// <summary>
	/// Replaces a mesh instance's active materials with blending copies and records them for
	/// fading. A <see cref="GeometryInstance3D.MaterialOverride"/> wins over per-surface
	/// overrides, so it is faded on its own when present; otherwise each surface is handled.
	/// Surfaces driven by another custom shader are skipped.
	/// </summary>
	private static void CollectMeshFadeMaterials(MeshInstance3D meshInstance,
		List<(GodotObject Object, string Property)> result)
	{
		if (meshInstance.MaterialOverride != null)
		{
			if (WallClippedMaterial.PrivateCopy(meshInstance.MaterialOverride, blending: true) is var (material, color))
			{
				meshInstance.MaterialOverride = material;
				result.Add((material, $"{color}:a"));
			}
			return;
		}

		int surfaceCount = meshInstance.Mesh.GetSurfaceCount();
		for (int surface = 0; surface < surfaceCount; surface++)
		{
			if (WallClippedMaterial.PrivateCopy(meshInstance.GetActiveMaterial(surface), blending: true) is not var (material, color))
			{
				continue;
			}

			meshInstance.SetSurfaceOverrideMaterial(surface, material);
			result.Add((material, $"{color}:a"));
		}
	}
}
