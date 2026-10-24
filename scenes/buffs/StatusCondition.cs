using Godot;

/// <summary>
/// Identity of a gameplay status such as poisoned or blinded. A <see cref="Buff"/> declares the condition it
/// imposes, <see cref="BuffController"/> reports when a condition starts and ends, and presentation (screen
/// effects, later HUD icons) maps conditions to visuals. Gameplay and presentation react to the condition
/// rather than to buff classes.
/// The authored <c>.tres</c> file is the identity and is compared by reference, so always reference a condition
/// as an external resource and never embed it: <c>ItemFactory</c>'s deep duplicate copies embedded
/// sub-resources but shares external ones. <see cref="Resource.ResourceName"/> is the display name.
/// </summary>
[GlobalClass]
public partial class StatusCondition : Resource
{
}