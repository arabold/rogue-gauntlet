using Godot;

/// <summary>
/// Punishes whoever strikes this actor in melee: each direct melee hit that damages it applies <see cref="Effect"/> to
/// the attacker, at <see cref="Chance"/>, rolled on the attacker's own stream. Ranged, spell and area hits are safe, so
/// the counter to such a monster is to fight it from a distance. The acid blob corrodes its attackers this way.
/// </summary>
[GlobalClass]
public partial class ContactRetaliationComponent : Node
{
	[Export] public HurtBoxComponent HurtBox { get; set; }
	/// <summary>The timed buff an attacker suffers, such as the acid blob's acid burn.</summary>
	[Export] public Buff Effect { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1;

	public override void _Ready()
	{
		if (HurtBox == null || Effect?.IsValid != true)
		{
			GD.PushError($"{GetParent()?.Name}/{Name}: assign HurtBox and a valid Effect; melee attackers go unpunished.");
			return;
		}
		HurtBox.ContactResolved += OnContactResolved;
	}

	private void OnContactResolved(in CombatContactFeedback feedback)
	{
		var contact = feedback.Context;
		if (feedback.Outcome != DamageOutcome.Damaged || contact.DeliveryKind != DamageDeliveryKind.DirectMelee) return;
		if (contact.OwnerNode is not IBuffTarget { Buffs: { } buffs } attacker || ReferenceEquals(attacker, HurtBox.GetParent())) return;
		if (Chance < 1 && (contact.Random?.Randf() ?? 1) >= Chance) return;
		var application = buffs.ApplyBuff(Effect);
		if (CombatTrace.Enabled) CombatTrace.Write(this, "retaliation.applied", ("key", Effect.RefreshKey), ("result", application.ToString()));
	}
}
