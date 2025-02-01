using Godot;

public interface IDamageable
{
	/// <summary>
	/// Called when the object takes damage
	/// </summary>
	void TakeDamage(float amount, Vector3 attackDirection);
}
