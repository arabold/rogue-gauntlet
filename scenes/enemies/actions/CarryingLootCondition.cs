using Godot;

/// <summary>
/// Holds while the actor carries loot it stole (<see cref="TheftComponent"/>), from <see cref="MinimumSeconds"/> after
/// the theft: the window right after a theft is the hero's chance to strike back before the thief can blink away.
/// </summary>
[Tool]
[GlobalClass]
public partial class CarryingLootCondition : MonsterActionCondition
{
	[Export(PropertyHint.Range, "0,10,0.05,suffix:s")] public float MinimumSeconds { get; set; }

	public override bool IsMet(EnemyContext context, Node3D target) =>
		context.Actor.TheftComponent is { IsCarrying: true } theft && theft.CarriedSeconds >= MinimumSeconds;

	public override void Validate(MonsterValidationContext context)
	{
		context.Check(MonsterValidationContext.IsNonnegative(MinimumSeconds), "CarryingLootCondition needs a nonnegative MinimumSeconds.");
		context.CheckWiring(MonsterValidationContext.Object<Node>(context.Actor, "TheftComponent") != null,
			"CarryingLootCondition: assign the actor's TheftComponent; without one it never holds.");
	}
}
