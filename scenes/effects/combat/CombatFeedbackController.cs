using Godot;

/// <summary>Coordinates optional presentation for contacts resolved by one actor's shared hurtbox.</summary>
[GlobalClass]
public partial class CombatFeedbackController : Node
{
	private bool _enabled = true;
	private CombatContactFeedbackHandler _worldHandler;
	[Export] public Node3D Actor { get; set; }
	[Export] public HurtBoxComponent HurtBox { get; set; }
	[Export] public MovementComponent Movement { get; set; }
	[Export] public ImpactSurfaceProfile SurfaceProfile { get; set; }
	[Export] public SurfaceAppearanceController SurfaceAppearance { get; set; }
	[Export] public CombatVisualMotionComponent VisualMotion { get; set; }
	[Export] public InvisibilityComponent Invisibility { get; set; }
	[Export] public CombatFeedbackProfile FallbackProfile { get; set; }
	[Export]
	public bool Enabled
	{
		get => _enabled;
		set { _enabled = value; if (!value) VisualMotion?.Clear(); }
	}

	public override void _Ready()
	{
		if (Actor == null || HurtBox == null)
		{
			GD.PushError($"{Name}: assign Actor and HurtBox to enable combat feedback.");
			return;
		}
		if (SurfaceProfile == null) SurfaceProfile = HurtBox.ImpactSurface;
		if (FallbackProfile != null && HurtBox.FeedbackProfile == null) HurtBox.FeedbackProfile = FallbackProfile;
		this.SubscribeUntilExit(HurtBox, h => h.ContactResolved += OnTargetContact, h => h.ContactResolved -= OnTargetContact);
		_worldHandler = OnWorldContact;
		SignalBus.CombatContactResolved += _worldHandler;
	}

	public override void _ExitTree()
	{
		if (_worldHandler != null) SignalBus.CombatContactResolved -= _worldHandler;
		VisualMotion?.Clear();
	}

	private void OnTargetContact(in CombatContactFeedback feedback) => PresentTarget(feedback);

	private void OnWorldContact(in CombatContactFeedback feedback)
	{
		if (!ReferenceEquals(feedback.Context.OwnerNode, Actor) || ReferenceEquals(feedback.Target, HurtBox)) return;
		if (!Enabled || feedback.Outcome != DamageOutcome.Damaged || feedback.Context.DeliveryKind != DamageDeliveryKind.DirectMelee
			|| feedback.Lethal || feedback.Settings.Enabled != true) return;
		float scale = feedback.Settings.ScaleFor(feedback.Outcome, feedback.Emphasis);
		float distance = feedback.Settings.LungeDistance * scale;
		bool triggered = Movement?.TryLunge(feedback.Context.ExecutionId, feedback.IncomingDirection,
			distance, feedback.Settings.MotionDuration) == true;
		CombatTrace.Write(this, "effect.lunge", ("execution", feedback.Context.ExecutionId.Sequence),
			("distance", distance), ("duration", feedback.Settings.MotionDuration),
			("speed", feedback.Settings.MotionDuration > 0 ? distance / feedback.Settings.MotionDuration : 0),
			("triggered", triggered));
	}

	private void PresentTarget(in CombatContactFeedback feedback)
	{
		if (!Enabled || feedback.ReactionSuppressed || Invisibility?.IsInvisible == true) return;
		var settings = feedback.Settings;
		if (!settings.Enabled || settings.ScaleFor(feedback.Outcome, feedback.Emphasis) <= 0) return;
		float scale = settings.ScaleFor(feedback.Outcome, feedback.Emphasis);
		bool damaging = feedback.Outcome == DamageOutcome.Damaged;
		if (damaging && feedback.Reaction is null or HitReaction.Flinch)
		{
			float flashAmount = settings.FlashAmount * scale;
			bool flashTriggered = SurfaceAppearance?.PulseHit(settings.FlashColor, flashAmount, settings.FlashDuration) == true;
			CombatTrace.Write(this, "effect.hit_flash", ("outcome", feedback.Outcome.ToString()),
				("amount", flashAmount), ("duration", settings.FlashDuration),
				("triggered", flashTriggered));
			PlayParticles(SurfaceProfile?.DamageParticles ?? HurtBox.HitEffect, feedback.Position,
				feedback.IncomingDirection, "damage", feedback.Outcome);
			if (feedback.Reaction == HitReaction.Flinch)
			{
				float distance = settings.RecoilDistance * scale;
				bool recoilTriggered = VisualMotion?.Recoil(feedback.IncomingDirection, distance, settings.MotionDuration) == true;
				CombatTrace.Write(this, "effect.visual_recoil", ("distance", distance),
					("duration", settings.MotionDuration), ("triggered", recoilTriggered));
			}
			return;
		}

		PackedScene particles = feedback.Outcome switch
		{
			DamageOutcome.Blocked or DamageOutcome.GuardBroken => SurfaceProfile?.GuardParticles,
			DamageOutcome.Parried => SurfaceProfile?.ParryParticles,
			DamageOutcome.Absorbed => SurfaceProfile?.GuardParticles,
			_ => null
		};
		PlayParticles(particles, feedback.Position, feedback.IncomingDirection,
			feedback.Outcome is DamageOutcome.Blocked or DamageOutcome.GuardBroken or DamageOutcome.Absorbed ? "guard" : "parry", feedback.Outcome);
	}

	private void PlayParticles(PackedScene scene, Vector3 position, Vector3 incoming, string cue, DamageOutcome outcome)
	{
		if (scene == null || !IsInsideTree())
		{
			CombatTrace.Write(this, "effect.particles", ("cue", cue), ("outcome", outcome.ToString()),
				("scene", scene?.ResourcePath ?? ""), ("amount", 0), ("triggered", false),
				("reason", scene == null ? "not_configured" : "outside_tree"));
			return;
		}
		Node world = GetTree().CurrentScene ?? GetTree().Root;
		var particles = ScenePool.Spawn<GpuParticles3D>(scene, world);
		particles.GlobalPosition = position - incoming.Normalized() * 0.35f;
		particles.ResetPhysicsInterpolation();
		particles.OneShot = true;
		particles.Restart();
		particles.Emitting = true;
		CombatTrace.Write(this, "effect.particles", ("cue", cue), ("outcome", outcome.ToString()),
			("scene", scene.ResourcePath), ("amount", particles.Amount), ("triggered", true));
	}
}
