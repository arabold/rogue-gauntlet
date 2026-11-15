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
	/// This attacker's point at <paramref name="distance"/> from the target, on its <see cref="GetRingBearing"/>.
	/// </summary>
	public Vector3 GetRingPoint(Node3D attacker, AttackSlotKind kind, float distance, float arcDegrees = 360) =>
		Target == null ? attacker?.GlobalPosition ?? Vector3.Zero : Target.GlobalPosition + GetRingBearing(attacker, kind, arcDegrees) * distance;

	/// <summary>
	/// This attacker's horizontal direction from the target: engaged attackers spread by arrival order over an arc of
	/// <paramref name="arcDegrees"/> centered behind the target's facing. 360 surrounds the target evenly, starting straight
	/// behind it; a narrower arc keeps every attacker at the target's back.
	/// </summary>
	public Vector3 GetRingBearing(Node3D attacker, AttackSlotKind kind, float arcDegrees = 360)
	{
		if (Target == null || !Track(attacker, out ulong id)) return Vector3.Back;
		int index = Ledger.Engage(id, kind);
		Vector3 rear = Target.GlobalBasis.Z.TryNormalizeHorizontal(out var back) ? back : Vector3.Back;
		return rear.Rotated(Vector3.Up, SpreadOffset(index, Ledger.EngagedCount, Mathf.DegToRad(Mathf.Clamp(arcDegrees, 0, 360))));
	}

	/// <summary>
	/// Radians from an arc's center for ring index <paramref name="index"/> of <paramref name="count"/>: a full circle
	/// shares it in equal steps, a partial arc spreads from one end to the other.
	/// </summary>
	public static float SpreadOffset(int index, int count, float arc) =>
		count <= 1 ? 0 : arc >= Mathf.Tau - 0.001f ? index * arc / count : (index / (float)(count - 1) - 0.5f) * arc;

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
		if (_attackers.TryAdd(id, attacker)) SetPhysicsProcess(true);
		return true;
	}
}