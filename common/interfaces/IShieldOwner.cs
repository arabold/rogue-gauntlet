/// <summary>An actor whose explicitly wired hurtbox can receive a magic guard.</summary>
public interface IShieldOwner
{
	HurtBoxComponent ShieldHurtBox { get; }
}
