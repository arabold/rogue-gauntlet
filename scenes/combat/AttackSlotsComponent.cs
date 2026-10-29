using Godot;
using System.Collections.Generic;

/// <summary>
/// Target-owned attack slots: monsters must hold one to start a wind-up against this actor, so only a
/// few attacks are ever coming at once. Waiting monsters take spread ring positions around the target.
/// Attackers forget themselves on death, exit and target loss; freed attackers are pruned here.
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
	private readonly List<ulong> _freed = [];
	private AttackSlotLedger Ledger => _ledger ??= new(Mathf.Max(0, MeleeSlots), Mathf.Max(0, RangedSlots), Mathf.Max(0, ReacquireDelay));

	public override void _Ready()
	{
		SetPhysicsProcess(_attackers.Count > 0);
	}

	public bool CanReserve(Node3D attacker, AttackSlotKind kind) => Track(attacker, out ulong id) && Ledger.CanReserve(id, kind);

	public bool TryReserve(Node3D attacker, AttackSlotKind kind)
	{
		if (!Track(attacker, out ulong id) || !Ledger.TryReserve(id, kind)) return false;
		if (CombatTrace.Enabled) CombatTrace.Write(this, "slots.reserved", ("attacker", CombatTrace.Identity(attacker)), ("kind", kind.ToString()),
			("held", Ledger.Held(kind)));
		return true;
	}

	/// <summary>Whether the attacker currently holds a slot; lets tools and tests observe turn-taking.</summary>
	public bool Holds(Node3D attacker) => attacker != null && Ledger.Holds(attacker.GetInstanceId());

	public void Release(Node3D attacker, bool yield = true)
	{
		if (attacker != null) Ledger.Release(attacker.GetInstanceId(), yield);
	}

	public void Forget(Node3D attacker)
	{
		if (attacker != null) Forget(attacker.GetInstanceId());
	}

	/// <summary>
	/// Evenly spread point at <paramref name="distance"/> around the target for this attacker's ring
	/// index. Rear-preferring attackers start behind the target's facing; others use a fixed world bearing.
	/// </summary>
	public Vector3 GetRingPoint(Node3D attacker, AttackSlotKind kind, float distance, bool preferRear)
	{
		if (Target == null || !Track(attacker, out ulong id)) return attacker?.GlobalPosition ?? Vector3.Zero;
		int index = Ledger.Engage(id, kind);
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
		if (_ledger == null || _attackers.Count == 0)
		{
			SetPhysicsProcess(false);
			return;
		}
		_ledger.Tick(delta);
		_freed.Clear();
		foreach (var (id, attacker) in _attackers)
			if (!GodotObject.IsInstanceValid(attacker)) _freed.Add(id);
		foreach (ulong id in _freed) Forget(id);
		if (_attackers.Count == 0) SetPhysicsProcess(false);
	}

	private void Forget(ulong id)
	{
		Ledger.Forget(id);
		_attackers.Remove(id);
	}

	private bool Track(Node3D attacker, out ulong id)
	{
		id = 0;
		if (attacker == null || !GodotObject.IsInstanceValid(attacker)) return false;
		id = attacker.GetInstanceId();
		_attackers[id] = attacker;
		SetPhysicsProcess(true);
		return true;
	}
}