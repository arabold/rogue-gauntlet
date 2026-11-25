using Godot;

/// <summary>
/// A patch of slime on the floor that holds its <see cref="Effect"/> on the hero while they stand in it, then dries up:
/// the gelatinous cube's trail and lob slow the hero, the spitting slime's pools poison them. Monsters wade through
/// unhindered. Saves leave its hold out, since a restored hero stands elsewhere. Pooled: spawning restarts its life
/// and its look.
/// </summary>
[GlobalClass]
public partial class GooPatch : Node3D, IPooledNode
{
	private static readonly StringName PlayerGroup = "player";

	/// <summary>The buff held on the hero for as long as they stand in the patch, whatever its own duration.</summary>
	[Export] public Buff Effect { get; set; }
	[Export(PropertyHint.Range, "0.2,5,0.05,suffix:m")] public float Radius { get; set; } = 1.1f;
	[Export(PropertyHint.Range, "0.5,30,0.5,suffix:s")] public float Lifetime { get; set; } = 6f;
	/// <summary>Seconds at the end of its life over which its visual shrinks away; it holds its effect until it is gone.</summary>
	[Export(PropertyHint.Range, "0,3,0.05,suffix:s")] public float ShrinkTime { get; set; } = 0.8f;
	/// <summary>The puddle shown on the floor; shrinks as the patch dries up. A patch under a slime track needs none.</summary>
	[Export] public Node3D Visual { get; set; }

	private BuffController _holding;
	private double _age;
	private Vector3 _visualScale = Vector3.One;

	public override void _Ready()
	{
		if (Effect == null) GD.PushError($"{Name}: assign the buff the patch holds.");
		if (Visual != null) _visualScale = Visual.Scale;
		ProcessPhysicsPriority = CombatPhysics.Damage;
		Restart();
	}

	public void OnSpawnedFromPool() => Restart();
	public void OnDespawnedToPool() => Release();
	public override void _ExitTree() => Release();

	public override void _PhysicsProcess(double delta)
	{
		_age += delta;
		if (_age >= Lifetime)
		{
			Release();
			SetPhysicsProcess(false);
			if (ScenePool.IsTracked(this)) ScenePool.Despawn(this); else QueueFree();
			return;
		}
		if (Visual != null && ShrinkTime > 0)
			Visual.Scale = _visualScale * (float)Mathf.Clamp((Lifetime - _age) / ShrinkTime, 0.05, 1);
		BuffController inside = GetTree().GetFirstNodeInGroup(PlayerGroup) is Node3D hero
			&& hero is IBuffTarget { Buffs: { } statuses } target && target.Health?.IsDead != true && Contains(hero.GlobalPosition)
			? statuses : null;
		if (inside == _holding) return;
		Release();
		if (inside != null && inside.SetPersistentBuff(Effect, this, true, narrate: false)) _holding = inside;
	}

	private bool Contains(Vector3 point)
	{
		Vector3 offset = point - GlobalPosition;
		return Mathf.Abs(offset.Y) < 1.5f && (offset with { Y = 0 }).Length() <= Radius;
	}

	private void Restart()
	{
		Release();
		_age = 0;
		if (Visual != null) Visual.Scale = _visualScale;
		SetPhysicsProcess(true);
	}

	private void Release()
	{
		if (GodotObject.IsInstanceValid(_holding) && _holding.IsInsideTree())
			_holding.SetPersistentBuff(Effect, this, false, narrate: false);
		_holding = null;
	}
}
