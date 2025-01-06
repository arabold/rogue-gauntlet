public class PlayerAction
{
	public string Id { get; }
	public string Name { get; }
	public float Duration { get; }
	public float Cooldown { get; }

	public PlayerAction(
		string id,
		string name,
		float duration,
		float cooldown = 0f)
	{
		Id = id;
		Name = name;
		Duration = duration;
		Cooldown = cooldown;
	}
}
