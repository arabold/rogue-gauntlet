using Godot;

/// <summary>
/// Exclusively drives the actor's attack pose from the combat clock. Locomotion and other
/// actions retain the authored AnimationTree; no competing mixers run during an attack.
/// </summary>
public partial class AttackAnimationComponent : Node
{
	[Export] public NodePath AnimationPlayerPath { get; set; }
	[Export] public NodePath AnimationTreePath { get; set; }
	[Export] public AttackAnimationProfile Profile { get; set; }
	[Export] public StringName ReturnState { get; set; } = "Idle";

	private AnimationPlayer _player;
	private AnimationTree _tree;
	private bool _treeWasActive;
	public bool IsPlaying { get; private set; }
	public StringName CurrentStyle { get; private set; } = "";

	/// <summary>Resolve lazily so class-model replacement in the parent's EnterTree has completed.</summary>
	public bool TryResolve(StringName style, out AttackPlayback playback)
	{
		playback = null;
		_player = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
		_tree = GetNodeOrNull<AnimationTree>(AnimationTreePath);
		if (_player == null || _tree == null || Profile == null
			|| !Profile.TryGetBinding(style, out var binding) || !_player.HasAnimation(binding.Clip))
		{
			return false;
		}
		double length = _player.GetAnimation(binding.Clip).Length;
		if (!double.IsFinite(length) || length <= 0) return false;
		playback = new AttackPlayback(style, binding.Clip, length, binding.ContactStart, binding.ContactEnd);
		return true;
	}

	/// <summary>Take ownership of playback only after all attack validation succeeds.</summary>
	public void Begin(AttackPlayback playback)
	{
		_treeWasActive = _tree.Active;
		_tree.Active = false;
		_player.Play(playback.Clip);
		_player.Pause();
		IsPlaying = true;
		CurrentStyle = playback.Style;
		Sample(playback, 0);
	}

	/// <summary>Evaluate the exact normalized attack pose without running clip method/audio tracks.</summary>
	public void Sample(AttackPlayback playback, double progress)
	{
		_player.Seek(Mathf.Clamp(progress, 0, 1) * playback.ClipLength, true, true);
	}

	/// <summary>Return the rig to its authored non-attack state machine, including hit/death transitions.</summary>
	public void End()
	{
		if (!IsPlaying) return;
		IsPlaying = false;
		CurrentStyle = "";
		if (GodotObject.IsInstanceValid(_player)) _player.Stop(true);
		if (GodotObject.IsInstanceValid(_tree))
		{
			_tree.Active = _treeWasActive;
			if (_treeWasActive && _tree.Get("parameters/playback").AsGodotObject() is AnimationNodeStateMachinePlayback state)
			{
				state.Start(ReturnState);
			}
		}
	}

	public override void _ExitTree() => End();
}