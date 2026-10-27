using System.Collections.Generic;
using System.Linq;

public enum AttackSlotKind { Melee, Ranged }

/// <summary>
/// Plain bookkeeping for how many attackers may wind up against one target at once. Attackers that
/// ask are engaged in arrival order, which gives each a stable ring index while it stays engaged. An
/// attacker that just released a slot yields for <c>reacquireDelay</c> only while another engaged
/// attacker is waiting for the same kind, so a lone monster keeps its normal cadence.
/// </summary>
public sealed class AttackSlotLedger(int meleeSlots, int rangedSlots, float reacquireDelay)
{
	private readonly Dictionary<ulong, AttackSlotKind> _holders = [];
	private readonly Dictionary<ulong, double> _cooling = [];
	private readonly Dictionary<ulong, AttackSlotKind> _wants = [];
	private readonly List<ulong> _engaged = [];

	public int EngagedCount => _engaged.Count;
	public bool Holds(ulong id) => _holders.ContainsKey(id);
	public int Held(AttackSlotKind kind) => _holders.Values.Count(held => held == kind);

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
		_holders[id] = kind;
		_cooling.Remove(id);
		return true;
	}

	/// <summary>Free the attacker's slot. <paramref name="yield"/> starts its reacquire delay; a failed start passes false.</summary>
	public void Release(ulong id, bool yield = true)
	{
		if (_holders.Remove(id) && yield && reacquireDelay > 0) _cooling[id] = reacquireDelay;
	}

	/// <summary>Drop every trace of an attacker that died, left or changed target.</summary>
	public void Forget(ulong id)
	{
		_holders.Remove(id);
		_cooling.Remove(id);
		_wants.Remove(id);
		_engaged.Remove(id);
	}

	/// <summary>Register interest and return the attacker's ring index among engaged attackers.</summary>
	public int Engage(ulong id, AttackSlotKind kind)
	{
		_wants[id] = kind;
		if (!_engaged.Contains(id)) _engaged.Add(id);
		return _engaged.IndexOf(id);
	}

	public void Tick(double delta)
	{
		foreach (ulong id in _cooling.Keys.ToList())
		{
			double remaining = _cooling[id] - delta;
			if (remaining > 0) _cooling[id] = remaining;
			else _cooling.Remove(id);
		}
	}

	private int Capacity(AttackSlotKind kind) => kind == AttackSlotKind.Melee ? meleeSlots : rangedSlots;

	private bool HasWaiter(AttackSlotKind kind, ulong except) => _engaged.Any(id => id != except
		&& !_holders.ContainsKey(id) && !_cooling.ContainsKey(id) && _wants[id] == kind);
}