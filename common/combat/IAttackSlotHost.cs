/// <summary>An actor whose attackers must take turns. Targets without slots accept every attack.</summary>
public interface IAttackSlotHost
{
	AttackSlotsComponent AttackSlots { get; }
}