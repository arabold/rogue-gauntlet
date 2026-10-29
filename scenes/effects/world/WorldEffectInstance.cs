using Godot;
using System.Collections.Generic;

/// <summary>Authored multi-layer effect with a local clock, bounded particle tails and optional skin request.</summary>
[GlobalClass]
public partial class WorldEffectInstance : Node3D
{
	[Signal] public delegate void FinishedEventHandler();
	[Export(PropertyHint.Range, "0.05,10,0.05")] public float Duration { get; set; } = 1.2f;
	[Export(PropertyHint.Range, "0,3,0.05")] public float EnterTime { get; set; } = 0.18f;
	[Export(PropertyHint.Range, "0.05,5,0.05")] public float ExitTime { get; set; } = 0.8f;
	[Export] public Curve Envelope { get; set; }
	[Export] public Curve SurfaceEnvelope { get; set; }
	[Export] public MeshInstance3D[] Layers { get; set; } = [];
	[Export] public GpuParticles3D[] Emitters { get; set; } = [];
	[Export] public SurfaceAppearanceProfile Surface { get; set; }
	[Export] public OmniLight3D AccentLight { get; set; }
	public WorldEffectDefinition Definition { get; internal set; }
	public bool IsEnding { get; private set; }
	public bool CompletedNaturally { get; private set; }
	public bool IsSuppressed { get; private set; }
	public float Elapsed { get; private set; }
	private readonly List<ShaderMaterial> _materials = [];
	private WorldEffectHost _host;
	private bool _started, _finished;
	private float _tail, _exitFrom, _amount, _enterFrom, _enteredAt;
	private float _lightEnergy;
	private Vector3 _lightOffset;

	public virtual bool Validate(SurfaceAppearanceController controller)
	{
		if (!float.IsFinite(Duration) || Duration <= 0 || !float.IsFinite(EnterTime) || EnterTime < 0
			|| !float.IsFinite(ExitTime) || ExitTime <= 0 || Envelope == null) return false;
		if (Surface != null && (controller == null || !controller.CanApply || Surface.Material?.Shader == null)) return false;
		foreach (var layer in Layers) if (layer?.Mesh == null || layer.MaterialOverride is not ShaderMaterial) return false;
		foreach (var emitter in Emitters) if (emitter == null || emitter.Lifetime > ExitTime) return false;
		return true;
	}
	public virtual void Start(WorldEffectHost host, uint seed)
	{
		_host = host; _started = true; VisualHelper.Mark(this);
		foreach (var layer in Layers)
		{
			var material = (ShaderMaterial)layer.MaterialOverride.Duplicate(); layer.MaterialOverride = material;
			material.SetShaderParameter("phase", (seed % 997) / 997f * Mathf.Tau); _materials.Add(material);
		}
		for (int i = 0; i < Emitters.Length; i++)
		{
			var emitter = Emitters[i];
			emitter.UseFixedSeed = true;
			emitter.Seed = (uint)GenerationRandom.DeriveSeed(seed, "vfx", i.ToString());
			emitter.Restart(true); emitter.Emitting = true;
		}
		if (AccentLight != null)
		{
			_lightEnergy = AccentLight.LightEnergy; _lightOffset = AccentLight.Position;
			// Radial effect lights share a world-space offset, independent of the actor's facing.
			AccentLight.TopLevel = true;
		}
		if (Surface != null && !host.SurfaceController.Acquire(this, Definition.Id, Surface, 0))
		{ Stop(true); return; }
		UpdateEnvelope(); ApplyVisuals();
	}
	public override void _Process(double delta)
	{
		if (!_started || _finished) return;
		float step = (float)delta * Mathf.Clamp(_host.PlaybackSpeed, 0, 2);
		foreach (var emitter in Emitters) emitter.SpeedScale = _host.PlaybackSpeed;
		if (step <= 0) return;
		Elapsed += step;
		if (IsEnding)
		{
			_tail += step; _amount = _exitFrom * (1 - Mathf.SmoothStep(0, ExitTime, _tail));
			if (_tail >= ExitTime) { CompletedNaturally = true; Finish(); return; }
		}
		else UpdateEnvelope();
		if (Definition.Lifetime == WorldEffectLifetime.Burst && Elapsed >= Duration && !IsEnding) Stop(false);
		ApplyVisuals();
	}
	/// <summary>Sample an initialized burst for workbench scrubbing; normal playback still owns completion.</summary>
	public void Seek(float seconds)
	{
		if (!_started || _finished || IsEnding || Definition.Lifetime != WorldEffectLifetime.Burst) return;
		Elapsed = Mathf.Clamp(seconds, 0, Duration); UpdateEnvelope(); ApplyVisuals();
	}
	/// <summary>Finish the sustained entry immediately for initial hidden state or terminal cleanup.</summary>
	public void EnterImmediately()
	{
		if (!_started || _finished || IsEnding) return;
		Elapsed = Mathf.Max(Elapsed, _enteredAt + EnterTime); UpdateEnvelope(); ApplyVisuals();
	}
	/// <summary>Reverse a retiring sustained effect without replacing its current material or restarting its visual clock.</summary>
	public void Resume()
	{
		if (!_started || _finished || !IsEnding) return;
		IsEnding = false; _tail = 0; _enterFrom = _amount; _enteredAt = Elapsed;
		foreach (var emitter in Emitters) if (!emitter.OneShot) emitter.Emitting = !IsSuppressed;
		UpdateEnvelope(); ApplyVisuals();
	}
	private void UpdateEnvelope()
	{
		_amount = Definition.Lifetime == WorldEffectLifetime.Sustained
			? (EnterTime <= 0 ? 1 : Mathf.Lerp(_enterFrom, 1, Mathf.SmoothStep(0, EnterTime, Elapsed - _enteredAt)))
			: SampleBurstEnvelope(Elapsed);
	}
	protected virtual float SampleBurstEnvelope(float elapsed) => Envelope.SampleBaked(Mathf.Clamp(elapsed / Duration, 0, 1));
	protected virtual float VisualTime => Elapsed;
	protected virtual void ApplyVisuals()
	{
		foreach (var material in _materials)
		{
			material.SetShaderParameter("effect_time", VisualTime);
			material.SetShaderParameter("envelope", IsSuppressed ? 0 : _amount);
		}
		// Cloak still arbitrates surface priority; suppressed skins retain their intent underneath it.
		if (Surface != null) _host.SurfaceController.SetAmount(this, SurfaceEnvelope?.SampleBaked(Mathf.Clamp(Elapsed / Duration, 0, 1)) ?? _amount);
		if (AccentLight != null)
		{
			AccentLight.GlobalPosition = GlobalPosition + _lightOffset;
			AccentLight.LightEnergy = IsSuppressed ? 0 : _lightEnergy * _amount;
		}
	}
	/// <summary>Hide attached geometry/lights and stop emission without dropping sustained ownership.</summary>
	public void SetSuppressed(bool suppressed)
	{
		if (_finished || IsSuppressed == suppressed) return;
		IsSuppressed = suppressed; Visible = !suppressed;
		foreach (var emitter in Emitters)
		{
			emitter.Visible = !suppressed;
			if (suppressed) emitter.Emitting = false;
			else if (!IsEnding && !emitter.OneShot) { emitter.Restart(true); emitter.Emitting = true; }
		}
		ApplyVisuals();
	}
	public void Stop(bool immediate)
	{
		if (_finished) return;
		if (immediate) { Finish(); return; }
		if (IsEnding) return;
		IsEnding = true; _exitFrom = _amount;
		foreach (var emitter in Emitters) emitter.Emitting = false;
	}
	private void Finish()
	{
		if (_finished) return;
		_finished = true; Visible = false;
		if (Surface != null && IsInstanceValid(_host?.SurfaceController)) _host.SurfaceController.Release(this);
		EmitSignalFinished(); QueueFree();
	}
	public override void _ExitTree()
	{
		if (!_finished && _started) Finish();
	}
}