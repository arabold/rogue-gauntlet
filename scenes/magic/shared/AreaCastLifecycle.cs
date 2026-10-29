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
	private sealed record PendingContact(Cast Cast, float Damage, AttackDefenseTraits Traits, float Push,
		Action<Node3D, DamageResolution> AfterContact, DamageElement Element, double Due);
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

	/// <summary>Schedule an area contact on the physics clock, or resolve it immediately.</summary>
	public void Schedule(Cast cast, float delay, float damage, AttackDefenseTraits traits, float push = 0,
		Action<Node3D, DamageResolution> afterContact = null, DamageElement element = DamageElement.None)
	{
		if (delay <= 0) { Resolve(cast, damage, traits, push, afterContact, element); return; }
		_pending.Add(new PendingContact(cast, damage, traits, push, afterContact, element, _clock + delay));
		_pending.Sort((a, b) => a.Due.CompareTo(b.Due));
		cast.Owner.SetPhysicsProcess(true);
	}

	/// <summary>Run due contacts; the owning node can stop processing when none remain.</summary>
	public bool Advance(double delta)
	{
		_clock += delta;
		while (_pending.Count > 0 && _pending[0].Due <= _clock)
		{
			var contact = _pending[0]; _pending.RemoveAt(0);
			Resolve(contact.Cast, contact.Damage, contact.Traits, contact.Push, contact.AfterContact, contact.Element);
		}
		return _pending.Count > 0;
	}

	/// <summary>Resolve occupants at contact time within the captured area and cancellation generation.</summary>
	public void Resolve(Cast cast, float damage, AttackDefenseTraits traits, float forcedPush = 0,
		Action<Node3D, DamageResolution> afterContact = null, DamageElement element = DamageElement.None)
	{
		if (!GodotObject.IsInstanceValid(cast.Owner) || !cast.Owner.IsInsideTree()
			|| !GodotObject.IsInstanceValid(cast.Caster) || !cast.Caster.IsInsideTree()
			|| cast.Caster.GetWorld3D() != cast.World) return;
		var targets = AreaSpellTargets.Capture(cast.Owner, cast.Caster, cast.Origin, cast.Radius, cast.World);
		if (targets == null) return;
		var identity = new AttackIdentity(cast.Caster.GetInstanceId(), ++_sequence);
		ulong generation = _generation;
		var contact = new DamageContext { ExecutionId = identity, SourceNode = cast.Owner, OwnerNode = cast.Caster,
			Faction = HurtBoxComponent.GetDamageSource(cast.Caster), Amount = damage, Random = cast.Random,
			DefenseTraits = traits, DeliveryKind = DamageDeliveryKind.AreaImpact, ForcedPush = forcedPush, Element = element };
		AreaSpellTargets.Resolve(targets, cast.World, cast.Origin, cast.Facing, contact,
			() => _generation == generation && cast.Owner.IsInsideTree(), afterContact);
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
