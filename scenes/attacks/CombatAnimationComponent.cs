using Godot;

/// <summary>Single token-owned pose player for attacks, defenses and forced reactions. Definitions and clips stay immutable.</summary>
public partial class CombatAnimationComponent : Node
{
	[Export] public NodePath AnimationPlayerPath { get; set; }
	[Export] public NodePath AnimationTreePath { get; set; }
	[Export] public AttackAnimationProfile Profile { get; set; }
	[Export] public StringName ReturnState { get; set; } = "Idle";
	private AnimationPlayer _player;
	private AnimationTree _tree;
	private object _owner;
	private bool _treeWasActive;
	private StringName _clip;
	private bool _loop;
	public bool IsPlaying => _owner != null;
	public StringName CurrentStyle { get; private set; } = "";

	private bool Resolve()
	{
		_player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		_tree = GetNodeOrNull<AnimationTree>(AnimationTreePath);
		return _player != null && _tree != null;
	}

	public bool HasClip(StringName clip) => !clip.IsEmpty && Resolve() && _player.HasAnimation(clip);

	public bool TryResolve(StringName style, out AttackPlayback playback)
	{
		playback = null;
		if (!Resolve() || Profile == null || !Profile.TryGetBinding(style, out var binding) || !HasClip(binding.Clip)) return false;
		double length = _player.GetAnimation(binding.Clip).Length;
		if (!double.IsFinite(length) || length <= 0) return false;
		double windUpLength = 0;
		if (!string.IsNullOrWhiteSpace(binding.WindUpClip))
		{
			if (!HasClip(binding.WindUpClip)) return false;
			windUpLength = _player.GetAnimation(binding.WindUpClip).Length;
			if (!double.IsFinite(windUpLength) || windUpLength <= 0) return false;
		}
		playback = new AttackPlayback(style, binding.Clip, length, binding.ContactStart, binding.ContactEnd,
			binding.WindUpClip, windUpLength);
		return true;
	}

	/// <summary>Reject competing pose owners. Action arbitration must release the previous owner first.</summary>
	public bool BeginPose(object owner, StringName clip, double blend = 0, bool loop = false)
	{
		if (owner == null || (_owner != null && !ReferenceEquals(owner, _owner)) || !HasClip(clip)) return false;
		if (_owner == null) _treeWasActive = _tree.Active;
		_owner = owner;
		_clip = clip;
		_loop = loop;
		_tree.Active = false;
		_player.Play(clip, customBlend: blend);
		_player.Pause();
		SamplePose(owner, 0);
		return true;
	}

	public double PoseLength(object owner) => ReferenceEquals(owner, _owner) ? _player.GetAnimation(_clip).Length : 0;

	/// <summary>Sample from the owning gameplay clock; no animation method/audio tracks or shared loop edits.</summary>
	public void SamplePose(object owner, double seconds)
	{
		if (!ReferenceEquals(owner, _owner) || _owner == null) return;
		double length = PoseLength(owner);
		if (length <= 0) return;
		_player.Seek(_loop ? seconds % length : Mathf.Clamp(seconds, 0, length), true, true);
	}

	public void EndPose(object owner)
	{
		if (!ReferenceEquals(owner, _owner) || _owner == null) return;
		_owner = null;
		CurrentStyle = "";
		if (GodotObject.IsInstanceValid(_player)) _player.Stop(true);
		if (GodotObject.IsInstanceValid(_tree))
		{
			_tree.Active = _treeWasActive;
			if (_treeWasActive && _tree.Get("parameters/playback").AsGodotObject() is AnimationNodeStateMachinePlayback state)
				state.Start(ReturnState);
		}
	}

	public void Begin(AttackPlayback playback)
	{
		if (BeginPose(this, playback.WindUpClipLength > 0 ? playback.WindUpClip : playback.Clip)) CurrentStyle = playback.Style;
	}

	/// <summary>Optional preparation and release clips share the execution clock; switching clips never changes contact timing.</summary>
	public void Sample(AttackPlayback playback, double progress)
	{
		progress = Mathf.Clamp(progress, 0, 1);
		if (playback.WindUpClipLength <= 0)
		{
			SamplePose(this, progress * playback.ClipLength);
			return;
		}
		bool preparing = progress < playback.ContactStart;
		StringName clip = preparing ? playback.WindUpClip : playback.Clip;
		// Sampling a stale execution cannot reacquire a pose from dodge, stagger or a newer action.
		if (!ReferenceEquals(_owner, this)) return;
		if (_clip != clip) BeginPose(this, clip);
		double seconds = preparing
			? progress / playback.ContactStart * playback.WindUpClipLength
			: (progress - playback.ContactStart) / System.Math.Max(.0001, 1 - playback.ContactStart) * playback.ClipLength;
		SamplePose(this, seconds);
	}
	public void End() => EndPose(this);
	public override void _ExitTree() { if (_owner != null) EndPose(_owner); }
}