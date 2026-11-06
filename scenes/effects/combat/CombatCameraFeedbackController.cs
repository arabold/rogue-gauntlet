using Godot;
using PhantomCamera.Noise;
using System.Linq;

/// <summary>Scene-local camera observer for resolved player contacts.</summary>
[GlobalClass]
public partial class CombatCameraFeedbackController : Node
{
	private Player _player;
	private PhantomCameraNoiseEmitter3D _emitter;
	private float _strongestImpulse;
	private bool _enabled = true;
	private CombatContactFeedbackHandler _contactHandler;

	[Export] public Node3D NoiseEmitter { get; set; }
	[Export(PropertyHint.Range, "0,3,0.01")] public float MaximumImpulse { get; set; } = 0.65f;
	[Export] public float NoiseAmplitudePerUnit { get; set; } = 30f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float BlockedImpulseScale { get; set; } = 0.4f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float ParriedImpulseScale { get; set; } = 0.7f;
	[Export]
	public bool Enabled
	{
		get => _enabled;
		set { _enabled = value; if (!value) _strongestImpulse = 0; }
	}

	public override void _Ready()
	{
		if (NoiseEmitter != null) _emitter = new PhantomCameraNoiseEmitter3D(NoiseEmitter);
		_contactHandler = OnContact;
		SignalBus.CombatContactResolved += _contactHandler;
		if (SignalBus.Instance != null)
			this.SubscribeUntilExit(SignalBus.Instance, b => b.PlayerSpawned += Bind, b => b.PlayerSpawned -= Bind);
		Bind(GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault());
	}

	public void Bind(Player player)
	{
		_player = player;
	}

	private void OnContact(in CombatContactFeedback feedback)
	{
		if (!Enabled || _player == null || !feedback.Settings.Enabled) return;
		bool incoming = ReferenceEquals(feedback.Target, _player.HurtBoxComponent);
		bool outgoing = ReferenceEquals(feedback.Context.OwnerNode, _player);
		if (!incoming && !outgoing) return;
		float scale = feedback.Settings.ScaleFor(feedback.Outcome, feedback.Emphasis);
		if (incoming && feedback.Reaction is HitReaction.Interrupted or HitReaction.Staggered) scale = Mathf.Max(scale, 1);
		float outcomeScale = feedback.Outcome switch
		{
			DamageOutcome.Blocked => BlockedImpulseScale,
			DamageOutcome.Parried => ParriedImpulseScale,
			_ => 1
		};
		float requestedImpulse = feedback.Settings.CameraImpulse * scale * outcomeScale;
		bool emitted = AddImpulse(requestedImpulse, out float actualImpulse, out string reason);
		string direction = incoming && outgoing ? "incoming_and_outgoing" : incoming ? "incoming" : "outgoing";
		CombatTrace.Write(this, "effect.camera_shake", ("direction", direction), ("outcome", feedback.Outcome.ToString()),
			("execution", feedback.Context.ExecutionId.Sequence), ("requested_impulse", requestedImpulse),
			("impulse", actualImpulse), ("noise_amplitude", emitted ? actualImpulse * NoiseAmplitudePerUnit : 0),
			("triggered", emitted), ("reason", reason));
	}

	private bool AddImpulse(float strength, out float appliedStrength, out string reason)
	{
		appliedStrength = 0;
		strength = Mathf.Min(MaximumImpulse, Mathf.Max(0, strength));
		if (_emitter == null) { reason = "missing_emitter"; return false; }
		if (strength <= 0) { reason = "zero_strength"; return false; }
		if (_emitter.IsEmitting() && strength < _strongestImpulse) { reason = "weaker_than_active_shake"; return false; }
		_strongestImpulse = strength;
		_emitter.Noise.Amplitude = strength * NoiseAmplitudePerUnit;
		_emitter.Emit();
		appliedStrength = strength;
		reason = "emitted";
		return true;
	}

	public override void _ExitTree()
	{
		if (_contactHandler != null) SignalBus.CombatContactResolved -= _contactHandler;
	}
}
