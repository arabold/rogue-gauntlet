using Godot;

public interface IInteractive
{
	public void OnPlayerNearby(Player player);
	public void OnPlayerLeft(Player player);

	/// <summary>
	/// Called when the player interacts with the object
	/// </summary>
	void Interact(Player actor);
}
