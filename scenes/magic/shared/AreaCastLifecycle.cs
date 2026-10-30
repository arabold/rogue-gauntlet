using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Shared capture, contact identity and visual lifetime for one area-spell node.</summary>
public sealed class AreaCastLifecycle
{
	public readonly record struct Cast(Node Owner, Node3D Caster, Vector3 Origin, Vector3 Facing,
		World3D World, float Radius, Node3D[] Targets, RandomNumberGenerator Random);
	private readonly List<Node3D> _visuals = [];
	private readonly List<PendingContact> _pending = [];
	/// <summary>One cast's contact, sweeping outward with its front; every actor it reaches counts once.</summary>
	private sealed record PendingContact(Cast Cast, AreaFront Front, AttackIdentity Identity, float Damage, AttackDefenseTraits Traits,
		float Push, Action<Node3D, DamageResolution> AfterContact, DamageElement Element, double Start)
	{
		public HashSet<Node3D> Reached { get; } = [];
	}
	private double _clock;
	private ulong _sequence, _generation;

	/// <summary>Capture eligible targets without mutating the world.</summary>
	public static bool TryCapture(Node owner, Node3D caster, Node3D visualRoot, RandomNumberGenerator random,
		float radius, out Cast cast)
	{
		cast = default;
		if (!owner.IsInsideTree() || !GodotObject.IsInstanceValid(caster) || !caster.IsInsideTree()
			|| !GodotObject.IsInstanceValid(visualRoot) || !visualRoot.IsInsideTree()
			|| visualRoot.GetWorld3D() != caster.GetWorld3D() || random == null) return false;
		Vector3 origin = caster.GlobalPosition, facing = -caster.GlobalBasis.Z;
		World3D world = caster.GetWorld3D();
		var targets = AreaSpellTargets.Capture(owner, caster, origin, radius, world);
		if (targets == null) return false;
		cast = new Cast(owner, caster, origin, facing, world, radius, targets, random);
		return true;
	}

	/// <summary>Track a spawned visual for focus cancellation.</summary>
	public void Track(Node3D visual)
	{
		_visuals.RemoveAll(v => !GodotObject.IsInstanceValid(v));
		_visuals.Add(visual);
	}

	/// <summary>
	/// Schedule an area contact on the physics clock. After <paramref name="delay"/> it follows the visual's
	/// <paramref name="front"/>, hitting each occupant once as the front reaches it; whatever reaches the front
	/// right away, such as everyone for an instant front without delay, is hit before this returns.
	/// </summary>
	public void Schedule(Cast cast, float delay, AreaFront front, float damage, AttackDefenseTraits traits, float push = 0,
		Action<Node3D, DamageResolution> afterContact = null, DamageElement element = DamageElement.None)
	{
		var contact = new PendingContact(cast, front with { Delay = front.Delay + delay },
			new AttackIdentity(cast.Caster.GetInstanceId(), ++_sequence), damage, traits, push, afterContact, element, _clock);
		ulong generation = _generation;
		if (Sweep(contact) || _generation != generation) return;
		_pending.Add(contact);
		cast.Owner.SetPhysicsProcess(true);
	}

	/// <summary>Advance pending fronts; the owning node can stop processing when none remain.</summary>
	public bool Advance(double delta)
	{
		_clock += delta;
		ulong generation = _generation;
		// Backwards, so contacts a callback schedules or a finished front's removal never shift the ones still due.
		for (int i = _pending.Count - 1; i >= 0 && _generation == generation; i--)
			if (Sweep(_pending[i]) && _generation == generation) _pending.RemoveAt(i);
		return _pending.Count > 0;
	}

	/// <summary>
	/// Hit the occupants the front has newly reached and report whether it has covered the whole area. An actor
	/// the front passes behind cover is spared rather than caught later when it steps out.
	/// </summary>
	private bool Sweep(PendingContact contact)
	{
		float reach = contact.Front.Reach((float)(_clock - contact.Start));
		if (reach > 0) Resolve(contact, contact.Cast.Radius * reach);
		return reach >= 1;
	}

	/// <summary>Resolve newly reached occupants within the captured area and cancellation generation.</summary>
	private void Resolve(PendingContact pending, float radius)
	{
		var cast = pending.Cast;
		if (!GodotObject.IsInstanceValid(cast.Owner) || !cast.Owner.IsInsideTree()
			|| !GodotObject.IsInstanceValid(cast.Caster) || !cast.Caster.IsInsideTree()
			|| cast.Caster.GetWorld3D() != cast.World) return;
		var targets = AreaSpellTargets.Capture(cast.Owner, cast.Caster, cast.Origin, radius, cast.World, pending.Reached);
		if (targets is not { Length: > 0 }) return;
		pending.Reached.UnionWith(targets);
		Hit(pending, targets);
	}

	/// <summary>Deliver one contact to each target; kept apart so ticks without new arrivals allocate no callback.</summary>
	private void Hit(PendingContact pending, Node3D[] targets)
	{
		var cast = pending.Cast;
		ulong generation = _generation;
		var contact = new DamageContext { ExecutionId = pending.Identity, SourceNode = cast.Owner, OwnerNode = cast.Caster,
			Faction = HurtBoxComponent.GetDamageSource(cast.Caster), Amount = pending.Damage, Random = cast.Random,
			DefenseTraits = pending.Traits, DeliveryKind = DamageDeliveryKind.AreaImpact, ForcedPush = pending.Push, Element = pending.Element };
		AreaSpellTargets.Resolve(targets, cast.World, cast.Origin, cast.Facing, contact,
			() => _generation == generation && cast.Owner.IsInsideTree(), pending.AfterContact);
	}

	/// <summary>Invalidate contacts and detach the current visual set before callbacks can reenter.</summary>
	public Node3D[] Cancel()
	{
		_generation++;
		_pending.Clear();
		var visuals = _visuals.ToArray(); _visuals.Clear();
		return visuals;
	}

	public static void ClearVisuals(IEnumerable<Node3D> visuals)
	{
		foreach (var visual in visuals)
			if (GodotObject.IsInstanceValid(visual)) { visual.Visible = false; visual.ProcessMode = Node.ProcessModeEnum.Disabled; visual.QueueFree(); }
	}
}
