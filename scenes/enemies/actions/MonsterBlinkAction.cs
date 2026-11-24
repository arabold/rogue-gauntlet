using Godot;

/// <summary>
/// A short teleport away from the target. The actor fades out where it stands (<see cref="Departure"/>) and, unless a
/// hit staggers it first, reappears (<see cref="Arrival"/>) at a spot it could have walked to,
/// <see cref="MinimumDistance"/> to <see cref="MaximumDistance"/> from the target and on its own side of it, within
/// <see cref="Spread"/> degrees. It deals no damage, so it is evasive: it may start while the actor retreats. A thief
/// carrying loot blinks this way (<see cref="CarryingLootCondition"/>).
/// </summary>
[Tool]
[GlobalClass]
public partial class MonsterBlinkAction : MonsterAction
{
	/// <summary>Spots tried for a landing before the blink gives up this time.</summary>
	private const int Attempts = 8;
	/// <summary>How far from a tried spot the nearest place the body fits may lie.</summary>
	private const float SnapRadius = 1.5f;

	[ExportGroup("Blink")]
	[Export(PropertyHint.Range, "1,30,0.5,suffix:m")] public float MinimumDistance { get; set; } = 8;
	[Export(PropertyHint.Range, "1,30,0.5,suffix:m")] public float MaximumDistance { get; set; } = 11;
	/// <summary>Degrees either side of the line from the target through the actor that a landing may lie.</summary>
	[Export(PropertyHint.Range, "0,180,5,suffix:°")] public float Spread { get; set; } = 90;
	/// <summary>The fade the actor leaves in; it lands when the fade ends, at once without one.</summary>
	[Export] public WorldEffectDefinition Departure { get; set; }
	[Export] public WorldEffectDefinition Arrival { get; set; }

	public override bool IsOffensive => false;

	public override bool IsAvailable(EnemyContext context) => context.Actor.ActionGate != null && context.Navigation?.IsAvailable == true;

	public override ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run)
	{
		run = null;
		var gate = context.Actor.ActionGate;
		if (gate == null || !GodotObject.IsInstanceValid(target) || !TryFindLanding(context, target, out var landing))
			return ActorActionRequestResult.Unavailable;
		var blink = new MonsterBlinkRun(this, target, context.Actor, landing);
		var result = gate.TryAcquire(ActorActionKind.Dash, ActorActionPhase.Active, blink.Interrupted, out var lease);
		if (result != ActorActionRequestResult.Success) return result;
		blink.Begin(lease);
		run = blink;
		return ActorActionRequestResult.Success;
	}

	/// <summary>A spot in the distance band on the actor's side of the target that the actor fits and could walk to.</summary>
	public bool TryFindLanding(EnemyContext context, Node3D target, out Vector3 landing)
	{
		landing = default;
		var navigation = context.Navigation;
		Vector3 center = target.GlobalPosition;
		Vector3 away = (context.Actor.GlobalPosition - center).TryNormalizeHorizontal(out var bearing) ? bearing : context.Actor.GlobalBasis.Z;
		float spread = Mathf.DegToRad(Spread);
		for (int attempt = 0; attempt < Attempts; attempt++)
		{
			Vector3 spot = center + away.Rotated(Vector3.Up, context.RandomRange(-spread, spread))
				* context.RandomRange(MinimumDistance, MaximumDistance);
			if (!navigation.TryFindTraversable(spot with { Y = context.Actor.GlobalPosition.Y }, SnapRadius, out var fit)) continue;
			float distance = fit.HorizontalDistanceTo(center);
			if (distance < MinimumDistance - SnapRadius || distance > MaximumDistance + SnapRadius || !navigation.CanTravelTo(fit)) continue;
			landing = fit;
			return true;
		}
		return false;
	}

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		context.Check(EffectiveTarget.Intent != SpellTargetIntent.Self, "a blink needs a target to blink away from.");
		context.Check(MonsterValidationContext.IsPositive(MinimumDistance) && MaximumDistance >= MinimumDistance && Spread is >= 0 and <= 180,
			"a blink needs 0 < MinimumDistance <= MaximumDistance and a Spread between 0 and 180.");
		context.CheckWiring(context.ActionGate != null, "a blink needs the actor's action gate.");
	}
}
