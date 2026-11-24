using Godot;

/// <summary>
/// Leaves a trail of patches (such as <see cref="GooPatch"/>) behind a moving actor, one every <see cref="Spacing"/>
/// metres it travels on the ground; the gelatinous cube drags its slime this way. The patches join the actor's world, so
/// floor regeneration clears them, and they come from the scene pool.
/// </summary>
[GlobalClass]
public partial class SlimeTrailComponent : Node
{
	[Export] public Node3D Actor { get; set; }
	[Export] public HealthComponent Health { get; set; }
	[Export] public PackedScene Patch { get; set; }
	[Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")] public float Spacing { get; set; } = 2.2f;

	private Vector3 _last;
	private float _travelled;

	public override void _Ready()
	{
		if (Actor == null || Patch == null || Health == null)
		{
			GD.PushError($"{Name}: a slime trail needs its actor, health and patch scene.");
			SetPhysicsProcess(false);
			return;
		}
		_last = Actor.GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Health.IsDead || !Actor.IsInsideTree()) return;
		Vector3 position = Actor.GlobalPosition;
		float step = (position - _last).Length();
		_last = position;
		// A placement or teleport is not travel: a spawn, a pool reuse or a blink leaves no trail behind it, and the
		// trail starts afresh where it lands.
		if (step > Spacing)
		{
			_travelled = 0;
			return;
		}
		_travelled += step;
		if (_travelled < Spacing) return;
		_travelled = 0;
		var patch = ScenePool.Spawn<Node3D>(Patch, Actor.GetParent());
		patch.GlobalPosition = position;
		patch.GlobalRotation = new Vector3(0, Actor.GlobalRotation.Y, 0);
		patch.ResetPhysicsInterpolation();
	}
}
