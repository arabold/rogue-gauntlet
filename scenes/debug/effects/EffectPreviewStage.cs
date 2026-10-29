using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Prepare production actors for visual inspection while retaining their real meshes, effects and animations.</summary>
public static class EffectPreviewStage
{
	public static void AddSubject(Node3D subject, Node parent, List<AnimationPlayer> animations)
	{
		subject.ProcessMode = Node.ProcessModeEnum.Disabled;
		if (subject is CharacterBody3D body) { body.CollisionLayer = body.CollisionMask = 0; body.Rotation = Vector3.Up * Mathf.Pi; }
		parent.AddChild(subject);
		if (subject is EnemyBase enemy) enemy.DeathComponent.LootTableComponent = null;
		subject.Visible = true; subject.RemoveFromGroup("player"); subject.RemoveFromGroup("enemy");
		bool actor = subject is Player or EnemyBase;
		foreach (var node in subject.FindChildren("*", "", true, false))
		{
			if (node is MeshInstance3D mesh && !VisualHelper.IsInHelperSubtree(mesh) && actor) mesh.Layers = subject is Player ? 1u << 2 : 1u << 3;
			if (node is Label3D label) { label.Visible = false; label.Layers = VisualHelper.DebugRenderLayer; }
			if (node is Sprite3D sprite) { sprite.Visible = false; sprite.Layers = VisualHelper.DebugRenderLayer; }
			if (node is AnimationTree tree) tree.Active = false;
			if (node is AnimationPlayer animation) { animation.ProcessMode = Node.ProcessModeEnum.Always; animations.Add(animation); }
			if (node is Light3D light && !VisualHelper.IsInHelperSubtree(light)) light.Visible = false;
		}
	}
	/// <summary>Rebuilds the shared stationary spell fixtures and returns their visual actors.</summary>
	public static void ResetSpellTargets(Node3D parent, PackedScene visualScene, List<Node3D> targets, List<AnimationPlayer> animations, int count, bool enemyGroup, float radius = 5f, Action<Node3D> configureTarget = null)
	{
		foreach (var target in targets) { parent.RemoveChild(target); target.QueueFree(); }
		targets.Clear(); animations.RemoveAll(a => !GodotObject.IsInstanceValid(a) || !a.IsInsideTree());
		for (int i = 0; i < count; i++)
		{
			float angle = Mathf.Tau * i / Mathf.Max(count, 1) + .4f;
			var target = new Node3D { Name = $"Target{i}", Position = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius) };
			if (enemyGroup) target.AddToGroup("enemy");
			parent.AddChild(target); targets.Add(target);
			configureTarget?.Invoke(target);
			var visual = visualScene.Instantiate<Node3D>(); AddSubject(visual, target, animations);
			var xray = visual.GetNodeOrNull<OcclusionXrayComponent>("OcclusionXrayComponent");
			if (xray != null) { xray.IsEnabled = false; xray.QueueFree(); }
		}
		PlayAnimations(animations, 0, 1);
	}
	public static void PlayAnimations(IEnumerable<AnimationPlayer> animations, int pose, float speed)
	{
		string[] fragments = pose switch { 1 => new[] { "Running", "Run", "Walk" }, 2 => new[] { "Melee_Attack", "Attack", "Spellcast" }, _ => new[] { "Idle" } };
		foreach (var animation in animations)
			foreach (string fragment in fragments)
			{
				var clip = animation.GetAnimationList().FirstOrDefault(n => n.Contains(fragment, StringComparison.OrdinalIgnoreCase));
				if (clip == null) continue;
				animation.Play(clip); animation.SpeedScale = speed; break;
			}
	}
	public static void ClearChildren(Node root)
	{
		foreach (var child in root.GetChildren()) { root.RemoveChild(child); child.QueueFree(); }
	}
}