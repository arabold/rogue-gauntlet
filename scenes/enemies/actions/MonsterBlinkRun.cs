using Godot;

/// <summary>
/// A started blink: holds the actor's action gate through its departure fade (<see cref="TeleportEffect"/>), then lands
/// and plays the arrival. A hit that takes the gate (a stagger) cancels it, and the actor stays where it was.
/// </summary>
public sealed class MonsterBlinkRun(MonsterBlinkAction action, Node3D target, EnemyBase actor, Vector3 landing)
	: MonsterActionRun(action, target, null)
{
	private ActorActionLease _lease;
	private WorldEffectInstance _departure;
	private bool _finished;

	public override bool IsFinished => _finished || !GodotObject.IsInstanceValid(actor) || !actor.ActionGate.IsLeaseCurrent(_lease);

	/// <summary>Starts the departure under <paramref name="lease"/>; without an effect host or fade the actor lands at once.</summary>
	public void Begin(ActorActionLease lease)
	{
		_lease = lease;
		actor.MovementComponent.Stop();
		if (CombatTrace.Enabled) CombatTrace.Write(actor, "blink.started", ("landing", landing));
		_departure = TeleportEffect.Play(actor, actor.WorldEffects, landing, action.Departure, action.Arrival, (uint)actor.GetInstanceId(),
			() => !_finished && actor.ActionGate.IsLeaseCurrent(_lease), Landed);
	}

	/// <summary>The gate took the actor back: a stagger stopped the blink before it landed.</summary>
	public void Interrupted(ActionInterruptReason reason)
	{
		_finished = true;
		if (GodotObject.IsInstanceValid(_departure)) _departure.Stop(immediate: true);
		if (CombatTrace.Enabled && GodotObject.IsInstanceValid(actor)) CombatTrace.Write(actor, "blink.interrupted", ("reason", reason.ToString()));
	}

	private void Landed()
	{
		_finished = true;
		if (CombatTrace.Enabled) CombatTrace.Write(actor, "blink.landed", ("position", landing));
		actor.ActionGate.Release(_lease);
	}

	public override void Cancel()
	{
		if (!IsFinished) actor.ActionGate.CancelCurrent(ActionInterruptReason.NewAction);
	}
}
