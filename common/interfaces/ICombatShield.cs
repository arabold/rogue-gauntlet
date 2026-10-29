/// <summary>A single actor-local damage pool installed on a hurtbox.</summary>
public interface ICombatShield
{
	float Absorb(float damage, DamageElement element);
	void Reflect(float absorbed, in DamageContext contact);
	void End();
}
