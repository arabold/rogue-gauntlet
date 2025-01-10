using Godot;

public interface IInteractive
{
	/// <summary>
	/// Called when the player interacts with the object
	/// </summary>
	void Interact(Player actor);
}
