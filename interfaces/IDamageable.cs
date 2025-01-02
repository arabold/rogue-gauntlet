using Godot;

public interface IDamageable
{
    void TakeDamage(int amount, Vector3 attackDirection); // Called when the object takes damage
}
