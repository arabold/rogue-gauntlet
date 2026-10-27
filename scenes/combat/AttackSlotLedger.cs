using System.Collections.Generic;

public enum AttackSlotKind { Melee, Ranged }

/// <summary>
/// Plain bookkeeping for how many attackers may wind up against one target at once. Attackers that
/// ask are engaged in arrival order, which gives each a stable ring index while it stays engaged. An
/// attacker that just released a slot yields for <c>reacquireDelay</c> only while another engaged
/// attacker is waiting for the same kind, so a lone monster keeps its normal cadence. Queries run every
/// frame per chasing monster, so they avoid allocations.
/// </summary>
public sealed class AttackSlotLedger(int meleeSlots, int rangedSlots, float reacquireDelay)
{
	private readonly Dictionary<ulong, AttackSlotKind> _holders = [];
	private readonly int[] _held = new int[2];
	private readonly Dictionary<ulong, double> _cooling = [];
	private readonly List<ulong> _cooled = [];
	// Engaged attackers in arrival order with the slot kind each wants; the order is the ring index.
	private readonly OrderedDictionary<ulong, AttackSlotKind> _engaged = [];

	public int EngagedCount => _engaged.Count;
	public bool Holds(ulong id) => _holders.ContainsKey(id);
	public int Held(AttackSlotKind kind) => _held[(int)kind];

	/// <summary>Engage the attacker and report whether a reservation would succeed right now.</summary>
	public bool CanReserve(ulong id, AttackSlotKind kind)
	{
		Engage(id, kind);
		if (Holds(id)) return true;
		if (Held(kind) >= Capacity(kind)) return false;
		return !_cooling.ContainsKey(id) || !HasWaiter(kind, id);
	}

	/// <summary>Idempotent for the current holder; a rejected request changes nothing but engagement.</summary>
	public bool TryReserve(ulong id, AttackSlotKind kind)
	{
		if (!CanReserve(id, kind)) return false;
		if (_holders.TryAdd(id, kind)) _held[(int)kind]++;
		_cooling.Remove(id);
		return true;
	}

	/// <summary>Free the attacker's slot. <paramref name="yield"/> starts its reacquire delay; a failed start passes false.</summary>
	public void Release(ulong id, bool yield = true)
	{
		if (!_holders.Remove(id, out var kind)) return;
		_held[(int)kind]--;
		if (yield && reacquireDelay > 0) _cooling[id] = reacquireDelay;
	}

	/// <summary>Drop every trace of an attacker that died, left or changed target.</summary>
	public void Forget(ulong id)
	{
		Release(id, yield: false);
		_cooling.Remove(id);
		_engaged.Remove(id);
	}

	/// <summary>Register interest and return the attacker's ring index among engaged attackers.</summary>
	public int Engage(ulong id, AttackSlotKind kind)
	{
		_engaged[id] = kind;
		return _engaged.IndexOf(id);
	}

	public void Tick(double delta)
	{
		if (_cooling.Count == 0) return;
		_cooled.Clear();
		_cooled.AddRange(_cooling.Keys);
		foreach (ulong id in _cooled)
		{
			double remaining = _cooling[id] - delta;
			if (remaining > 0) _cooling[id] = remaining;
			else _cooling.Remove(id);
		}
	}

	private int Capacity(AttackSlotKind kind) => kind == AttackSlotKind.Melee ? meleeSlots : rangedSlots;

	private bool HasWaiter(AttackSlotKind kind, ulong except)
	{
		for (int i = 0; i < _engaged.Count; i++)
		{
			var (id, wants) = _engaged.GetAt(i);
			if (id != except && wants == kind && !_holders.ContainsKey(id) && !_cooling.ContainsKey(id)) return true;
		}
		return false;
	}
}