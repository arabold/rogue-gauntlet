using Godot;

/// <summary>Synchronizes locomotion animation playback with a moved actor's resolved ground speed.</summary>
[GlobalClass]
public partial class LocomotionAnimationComponent : Node
{
	[Export] public MovementComponent Movement { get; set; }
	[Export] public AnimationTree AnimationTree { get; set; }
	[Export(PropertyHint.Range, "0.1,10,0.1")] public float NaturalLocomotionSpeed { get; set; } = 2.7f;
	[Export(PropertyHint.Range, "0.1,10,0.1")] public float WalkNaturalSpeed { get; set; } = 1.5f;
	[Export(PropertyHint.Range, "0.1,20,0.1")] public float BlendStartSpeed { get; set; } = 1.5f;
	[Export(PropertyHint.Range, "0.1,20,0.1")] public float BlendEndSpeed { get; set; } = 5.5f;
	[Export] public StringName TimeScaleParameter { get; set; } = "parameters/Walking/TimeScale/scale";
	[Export] public StringName BlendParameter { get; set; } = "parameters/Walking/Blend2/blend_amount";
	[Export] public bool BlendWalkingAndRunning { get; set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (Movement == null || AnimationTree == null)
		{
			GD.PushWarning($"{Name}: locomotion animation requires Movement and AnimationTree references.");
			SetPhysicsProcess(false);
			return;
		}

		ProcessPhysicsPriority = CombatPhysics.MovementPresentation;
		ConfigureLocomotionLoops();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!GodotObject.IsInstanceValid(Movement) || !GodotObject.IsInstanceValid(AnimationTree)
			|| NaturalLocomotionSpeed <= 0 || TimeScaleParameter.IsEmpty) return;

		float speed = Movement.GroundSpeed;
		float blend = BlendWalkingAndRunning
			? Mathf.Clamp((speed - BlendStartSpeed) / Mathf.Max(0.01f, BlendEndSpeed - BlendStartSpeed), 0f, 1f)
			: 1f;
		if (BlendWalkingAndRunning && !BlendParameter.IsEmpty)
			AnimationTree.Set(BlendParameter, blend);

		float naturalSpeed = BlendWalkingAndRunning
			? Mathf.Lerp(WalkNaturalSpeed, NaturalLocomotionSpeed, blend)
			: NaturalLocomotionSpeed;
		AnimationTree.Set(TimeScaleParameter, speed > 0.05f ? speed / naturalSpeed : 0f);
	}

	/// <summary>Loops authored walk/run clips on a private animation library for this actor instance.</summary>
	private void ConfigureLocomotionLoops()
	{
		if (AnimationTree == null) return;
		var animationPlayer = AnimationTree.GetNodeOrNull<AnimationPlayer>(AnimationTree.AnimPlayer);
		if (animationPlayer == null) return;

		foreach (StringName libraryName in animationPlayer.GetAnimationLibraryList())
		{
			AnimationLibrary shared = animationPlayer.GetAnimationLibrary(libraryName);
			if (shared == null) continue;
			var instanceLibrary = shared.Duplicate(true) as AnimationLibrary;
			if (instanceLibrary == null) continue;
			animationPlayer.RemoveAnimationLibrary(libraryName);
			animationPlayer.AddAnimationLibrary(libraryName, instanceLibrary);
		}

		CharacterModel.ConfigureAnimationLoops(animationPlayer);
	}
}
