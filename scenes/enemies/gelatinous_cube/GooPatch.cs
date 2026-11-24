using System.Collections.Generic;
using Godot;

/// <summary>
/// A patch of slime on the floor that slows the hero while they stand in it, then dries up. The gelatinous cube leaves
/// them behind it (<see cref="SlimeTrailComponent"/>) and its lobbed goo leaves one where it lands. Monsters wade through
/// unhindered. Pooled: spawning restarts its life and its look.
/// </summary>
[GlobalClass]
public partial class GooPatch : Node3D, IPooledNode
{
	private static readonly StringName PlayerGroup = "player";

	/// <summary>The slow it holds on the hero while they stand in it; their slow component sets its speed.</summary>
	[Export] public StatusCondition SlowCondition { get; set; }
	[Export(PropertyHint.Range, "0.2,5,0.05,suffix:m")] public float Radius { get; set; } = 1.1f;
	[Export(PropertyHint.Range, "0.5,30,0.5,suffix:s")] public float Lifetime { get; set; } = 6f;
	/// <summary>Seconds at the end of its life over which its visual shrinks away; it slows until it is gone.</summary>
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float ShrinkTime { get; set; } = 0.8f;
	/// <summary>The puddle shown on the floor; shrinks as the patch dries up.</summary>
	[Export] public Node3D Visual { get; set; }

	private readonly HashSet<BuffController> _slowed = [];
	private readonly List<BuffController> _released = [];
	private double _age;
	private Vector3 _visualScale = Vector3.One;

	public bool IsSlowing(BuffController statuses) => _slowed.Contains(statuses);

	public override void _Ready()
	{
		if (SlowCondition == null) GD.PushError($"{Name}: assign the slow condition the patch holds.");
		if (Visual != null) _visualScale = Visual.Scale;
		ProcessPhysicsPriority = CombatPhysics.Damage;
		Restart();
	}

	public void OnSpawnedFromPool() => Restart();
	public void OnDespawnedToPool() => ReleaseAll();
	public override void _ExitTree() => ReleaseAll();

	public override void _PhysicsProcess(double delta)
	{
		_age += delta;
		if (_age >= Lifetime)
		{
			ReleaseAll();
			SetPhysicsProcess(false);
			if (ScenePool.IsTracked(this)) ScenePool.Despawn(this); else QueueFree();
			return;
		}
		if (Visual != null && ShrinkTime > 0)
			Visual.Scale = _visualScale * (float)Mathf.Clamp((Lifetime - _age) / ShrinkTime, 0.05, 1);
		_released.Clear();
		_released.AddRange(_slowed);
		foreach (Node node in GetTree().GetNodesInGroup(PlayerGroup))
		{
			if (node is not Node3D actor || node is not IBuffTarget { Buffs: { } statuses } target || target.Health?.IsDead == true) continue;
			Vector3 offset = actor.GlobalPosition - GlobalPosition;
			bool inside = Mathf.Abs(offset.Y) < 1.5f && (offset with { Y = 0 }).Length() <= Radius;
			if (!inside) continue;
			_released.Remove(statuses);
			if (_slowed.Add(statuses)) statuses.SetPersistentStatus(SlowCondition, this, true, narrate: false);
		}
		foreach (var statuses in _released) Release(statuses);
	}

	private void Restart()
	{
		ReleaseAll();
		_age = 0;
		if (Visual != null) Visual.Scale = _visualScale;
		SetPhysicsProcess(true);
	}

	private void Release(BuffController statuses)
	{
		_slowed.Remove(statuses);
		if (GodotObject.IsInstanceValid(statuses) && statuses.IsInsideTree())
			statuses.SetPersistentStatus(SlowCondition, this, false, narrate: false);
	}

	private void ReleaseAll()
	{
		foreach (var statuses in new List<BuffController>(_slowed)) Release(statuses);
		_slowed.Clear();
	}
}
