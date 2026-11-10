/// <summary>An actor that can receive actor-neutral timed effects.</summary>
public interface IBuffTarget
{
	HealthComponent Health { get; }
	BuffController Buffs { get; }
	IStatModifierTarget Modifiers { get; }
}
