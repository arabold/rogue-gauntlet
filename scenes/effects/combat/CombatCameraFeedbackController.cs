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
	private float _defaultGrowthTime, _defaultDuration, _defaultDecayTime;
	private bool _enabled = true;
	private CombatContactFeedbackHandler _contactHandler;
	private CameraImpulseRequestHandler _impulseHandler;

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
		if (NoiseEmitter != null)
		{
			_emitter = new PhantomCameraNoiseEmitter3D(NoiseEmitter);
			_defaultGrowthTime = _emitter.GrowthTime;
			_defaultDuration = _emitter.Duration;
			_defaultDecayTime = _emitter.DecayTime;
		}
		_contactHandler = OnContact;
		SignalBus.CombatContactResolved += _contactHandler;
		_impulseHandler = OnCameraImpulseRequested;
		SignalBus.CameraImpulseRequested += _impulseHandler;
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

	private bool AddImpulse(float strength, out float appliedStrength, out string reason,
		float? growthTime = null, float? duration = null, float? decayTime = null)
	{
		appliedStrength = 0;
		strength = Mathf.Min(MaximumImpulse, Mathf.Max(0, strength));
		if (_emitter == null) { reason = "missing_emitter"; return false; }
		if (strength <= 0) { reason = "zero_strength"; return false; }
		if (_emitter.IsEmitting() && strength < _strongestImpulse) { reason = "weaker_than_active_shake"; return false; }
		_strongestImpulse = strength;
		_emitter.GrowthTime = ResolveTime(growthTime, _defaultGrowthTime);
		_emitter.Duration = ResolveTime(duration, _defaultDuration);
		_emitter.DecayTime = ResolveTime(decayTime, _defaultDecayTime);
		_emitter.Noise.Amplitude = strength * NoiseAmplitudePerUnit;
		_emitter.Emit();
		appliedStrength = strength;
		reason = "emitted";
		return true;
	}

	private static float ResolveTime(float? requested, float fallback) =>
		requested is float value && float.IsFinite(value) && value >= 0 ? value : fallback;

	private void OnCameraImpulseRequested(in CameraImpulseRequest request)
	{
		if (!Enabled || _player == null || !GodotObject.IsInstanceValid(_player) || !float.IsFinite(request.Radius)
			|| request.Radius <= 0 || !float.IsFinite(request.Strength) || request.Strength <= 0) return;
		float distance = _player.GlobalPosition.HorizontalDistanceTo(request.Position);
		if (distance >= request.Radius) return;
		float requested = request.Strength * (1 - distance / request.Radius);
		bool emitted = AddImpulse(requested, out float actual, out string reason,
			request.GrowthTime, request.Duration, request.DecayTime);
		CombatTrace.Write(this, "effect.camera_shake", ("direction", "ground_impact"), ("requested_impulse", requested),
			("impulse", actual), ("noise_amplitude", emitted ? actual * NoiseAmplitudePerUnit : 0),
			("triggered", emitted), ("reason", reason));
	}

	public override void _ExitTree()
	{
		if (_contactHandler != null) SignalBus.CombatContactResolved -= _contactHandler;
		if (_impulseHandler != null) SignalBus.CameraImpulseRequested -= _impulseHandler;
	}
}
