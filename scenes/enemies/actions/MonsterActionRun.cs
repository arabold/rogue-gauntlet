using Godot;

/// <summary>
/// The running state of one started <see cref="MonsterAction"/>. The behavior host owns at most one run, ticks it while
/// it lasts and releases its attack slot when it ends, so the host never needs to know what kind of action it is.
/// </summary>
public abstract class MonsterActionRun(MonsterAction action, Node3D target, AttackSlotsComponent heldSlots)
{
	public MonsterAction Action { get; } = action;
	public Node3D Target { get; } = target;
	/// <summary>Slots reserved for this run, released once when it ends.</summary>
	public AttackSlotsComponent HeldSlots { get; private set; } = heldSlots;
	public abstract bool IsFinished { get; }

	/// <summary>Per-frame upkeep while running, such as facing through a wind-up or steering a dash.</summary>
	public virtual void Tick(EnemyContext context, double delta) { }

	/// <summary>Stop the executor early; a finished run ignores it.</summary>
	public abstract void Cancel();

	/// <summary>Return the held slot to the target exactly once.</summary>
	public void ReleaseSlots(Node3D actor)
	{
		if (GodotObject.IsInstanceValid(HeldSlots)) HeldSlots.Release(actor);
		HeldSlots = null;
	}

	/// <summary>Drop the held slot without yielding, when the actor dies or leaves the tree.</summary>
	public void ForgetSlots(Node3D actor)
	{
		if (GodotObject.IsInstanceValid(HeldSlots)) HeldSlots.Forget(actor);
		HeldSlots = null;
	}
}
