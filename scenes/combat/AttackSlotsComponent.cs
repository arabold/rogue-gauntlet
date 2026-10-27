using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Target-owned attack slots: monsters must hold one to start a wind-up against this actor, so only a
/// few attacks are ever coming at once. Waiting monsters take spread ring positions around the target.
/// Dead or freed attackers are pruned every physics frame; attackers forget themselves on target loss.
/// Counts are snapshotted when the component first serves a request.
/// </summary>
[GlobalClass]
public partial class AttackSlotsComponent : Node
{
	/// <summary>The actor these slots protect; ring positions surround it.</summary>
	[Export] public Node3D Target { get; set; }
	[Export] public int MeleeSlots { get; set; } = 2;
	[Export] public int RangedSlots { get; set; } = 1;

	/// <summary>Seconds a monster that just attacked yields its turn while another one waits.</summary>
	[Export] public float ReacquireDelay { get; set; } = 0.75f;

	private AttackSlotLedger _ledger;
	private readonly Dictionary<ulong, Node3D> _attackers = [];
	private AttackSlotLedger Ledger => _ledger ??= new(Mathf.Max(0, MeleeSlots), Mathf.Max(0, RangedSlots), Mathf.Max(0, ReacquireDelay));

	public bool CanReserve(Node3D attacker, AttackSlotKind kind) => Track(attacker) && Ledger.CanReserve(attacker.GetInstanceId(), kind);

	public bool TryReserve(Node3D attacker, AttackSlotKind kind)
	{
		if (!Track(attacker) || !Ledger.TryReserve(attacker.GetInstanceId(), kind)) return false;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "slots.reserved", ("attacker", CombatTrace.Identity(attacker)), ("kind", kind.ToString()),
			("held", Ledger.Held(kind)));
		return true;
	}

	public bool Holds(Node3D attacker) => attacker != null && Ledger.Holds(attacker.GetInstanceId());

	public void Release(Node3D attacker, bool yield = true)
	{
		if (attacker != null) Ledger.Release(attacker.GetInstanceId(), yield);
	}

	public void Forget(Node3D attacker)
	{
		if (attacker == null) return;
		ulong id = attacker.GetInstanceId();
		Ledger.Forget(id);
		_attackers.Remove(id);
	}

	/// <summary>
	/// Evenly spread point at <paramref name="distance"/> around the target for this attacker's ring
	/// index. Rear-preferring attackers start behind the target's facing; others use a fixed world bearing.
	/// </summary>
	public Vector3 GetRingPoint(Node3D attacker, AttackSlotKind kind, float distance, bool preferRear)
	{
		if (Target == null || !Track(attacker)) return attacker?.GlobalPosition ?? Vector3.Zero;
		int index = Ledger.Engage(attacker.GetInstanceId(), kind);
		Vector3 bearing = Vector3.Back;
		if (preferRear)
		{
			bearing = Target.GlobalTransform.Basis.Z;
			bearing.Y = 0;
			bearing = bearing.LengthSquared() > 0.0001f ? bearing.Normalized() : Vector3.Back;
		}
		float step = Mathf.Tau / Mathf.Max(1, Ledger.EngagedCount);
		return Target.GlobalPosition + bearing.Rotated(Vector3.Up, index * step) * distance;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_ledger == null) return;
		_ledger.Tick(delta);
		foreach (var (id, attacker) in _attackers.ToList())
			if (!GodotObject.IsInstanceValid(attacker) || attacker is EnemyBase { IsDead: true }) Forget(id);
	}

	private void Forget(ulong id)
	{
		Ledger.Forget(id);
		_attackers.Remove(id);
	}

	private bool Track(Node3D attacker)
	{
		if (attacker == null || !GodotObject.IsInstanceValid(attacker)) return false;
		_attackers[attacker.GetInstanceId()] = attacker;
		return true;
	}
}