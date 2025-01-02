using Godot;
using System;

public class PlayerAction
{
	public string Id { get; }
	public string Name { get; }
	public PlayerState State { get; }
	public string AnimationName { get; }
	public float Duration { get; }
	public float Cooldown { get; }
	public Action OnStart { get; set; }
	public Action OnEnd { get; set; }

	public PlayerAction(
		string id,
		string name,
		PlayerState state,
		string animationName,
		float duration,
		float cooldown = 0f,
		Action onStart = null,
		Action onEnd = null)
	{
		Id = id;
		Name = name;
		State = state;
		AnimationName = animationName;
		Duration = duration;
		Cooldown = cooldown;
		OnStart = onStart;
		OnEnd = onEnd;
	}
}
