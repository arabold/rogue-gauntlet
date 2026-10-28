using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>A focused fireball delivery. Each flight is restricted to its captured target.</summary>
public partial class FireballSpell : Node3D
{
	[Export] public PackedScene ProjectileScene { get; set; }
	[Export] public Node3D Caster { get; set; }
	[Export] public Node3D DeliveryRoot { get; set; }
	[Export] public float Radius { get; set; } = 9f;
	[Export] public float Speed { get; set; } = 13f;
	[Export] public int[] TargetCounts { get; set; } = [1, 2, 3];
	[Export] public float[] DamageByLevel { get; set; } = [8f, 11f, 14f];
	[Export] public float[] VisualScaleByLevel { get; set; } = [.58f, 1.15f, 1.8f];
	[Export] public float[] ImpactScaleByLevel { get; set; } = [1.1f, 1.45f, 1.9f];
	private readonly RandomNumberGenerator _targetRandom = new();
	private readonly RandomNumberGenerator _visualRandom = new();

	/// <summary>Samples without replacement; cosmetic filler never advances the target RNG.</summary>
	public int Cast(int level, IEnumerable<Node3D> candidates, ulong seed)
	{
		if (level < 1 || level > TargetCounts.Length || level > DamageByLevel.Length || level > VisualScaleByLevel.Length || level > ImpactScaleByLevel.Length
			|| ProjectileScene == null || Caster == null || DeliveryRoot == null || !Caster.IsInsideTree() || !DeliveryRoot.IsInsideTree() || Radius < 3f || Speed <= 0) return 0;
		int count = TargetCounts[level - 1];
		if (count < 1 || DamageByLevel[level - 1] < 0 || VisualScaleByLevel[level - 1] <= 0 || ImpactScaleByLevel[level - 1] <= 0) return 0;
		_targetRandom.Seed = seed;
		_visualRandom.Seed = seed ^ 0xB33F17UL;
		var targets = candidates.Where(t => GodotObject.IsInstanceValid(t) && t.IsInsideTree()
			&& t.IsInGroup("enemy") && (t.GetNodeOrNull<HealthComponent>("HealthComponent") is not { IsDead: true })
			&& Caster.GlobalPosition.DistanceTo(t.GlobalPosition) <= Radius
			&& WorldSight.IsClear(Caster.GetWorld3D(), Caster.GlobalPosition + Vector3.Up, t.GlobalPosition + Vector3.Up))
			.Distinct().OrderBy(t => t.GetPath().ToString(), System.StringComparer.Ordinal).ToList();
		var captured = new List<(Node3D Owner, Vector3 Position)>();
		while (targets.Count > 0 && captured.Count < count)
		{
			int index = _targetRandom.RandiRange(0, targets.Count - 1);
			Node3D target = targets[index]; targets.RemoveAt(index);
			captured.Add((target, target.GlobalPosition + Vector3.Up));
		}
		Vector3 origin = Caster.GlobalPosition + Vector3.Up;
		float damage = DamageByLevel[level - 1];
		float scale = VisualScaleByLevel[level - 1];
		float impactScale = ImpactScaleByLevel[level - 1];
		foreach (var target in captured) Launch(origin, target.Position, target.Owner, damage, scale, impactScale);
		for (int i = captured.Count; i < count; i++)
		{
			float angle = _visualRandom.RandfRange(0, Mathf.Tau);
			float distance = _visualRandom.RandfRange(3f, Radius);
			Launch(origin, origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance, null, 0, scale, impactScale);
		}
		return captured.Count;
	}

	/// <summary>Preview a harmless flight into a wall through the same projectile impact path.</summary>
	public bool PreviewWall(int level, Vector3 destination)
	{
		if (level < 1 || level > VisualScaleByLevel.Length || level > ImpactScaleByLevel.Length
			|| ProjectileScene == null || Caster == null || DeliveryRoot == null || !Caster.IsInsideTree() || !DeliveryRoot.IsInsideTree()) return false;
		Vector3 origin = Caster.GlobalPosition + Vector3.Up;
		if (origin.DistanceSquaredTo(destination) < 0.01f || Speed <= 0) return false;
		Launch(origin, destination, null, 0, VisualScaleByLevel[level - 1], ImpactScaleByLevel[level - 1]);
		return true;
	}

	private void Launch(Vector3 origin, Vector3 destination, Node target, float damage, float scale, float impactScale)
	{
		var projectile = ProjectileScene.Instantiate<Projectile>();
		DeliveryRoot.AddChild(projectile);
		projectile.RestrictToTarget(target, impactScale);
		Vector3 displacement = destination - origin;
		projectile.Initialize(origin, displacement.Normalized(), Speed, displacement.Length(), damage, damage, 0, Caster);
		// The collision shape remains unchanged as the visual grows.
		foreach (string name in new[] { "RibbonTrail", "HotRibbon" })
			if (projectile.GetNodeOrNull<MagicRibbonTrail>(name) is { } trail) trail.Width *= Mathf.Lerp(.8f, 1.4f, Mathf.Clamp((scale - .58f) / 1.22f, 0f, 1f));
		foreach (string name in new[] { "Core", "HeatShell", "FlameWake", "OuterFlames", "SmokeWake", "Embers", "Light" })
			if (projectile.GetNodeOrNull<Node3D>($"Pivot/{name}") is Node3D visual) visual.Scale = Vector3.One * scale;
	}
}