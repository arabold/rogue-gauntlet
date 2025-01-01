using System.Collections.Generic;

public class ActionRegistry
{
    private Dictionary<string, PlayerAction> _actions = new();

    public void RegisterAction(PlayerAction action)
    {
        _actions[action.Id] = action;
    }

    public PlayerAction GetAction(string actionId)
    {
        return _actions.TryGetValue(actionId, out var action) ? action : null;
    }

    public IEnumerable<string> GetAllActionIds() => _actions.Keys;

    public static ActionRegistry CreateDefault()
    {
        var registry = new ActionRegistry();

        registry.RegisterAction(new PlayerAction(
            "quick_attack",
            "Quick Attack",
            PlayerState.QuickAttacking,
            "1H_Melee_Attack_Chop",
            duration: 0.5f,
            cooldown: 0f
        ));

        registry.RegisterAction(new PlayerAction(
            "heavy_attack",
            "Heavy Attack",
            PlayerState.HeavyAttacking,
            "1H_Melee_Attack_Slice_Horizontal",
            duration: 1.2f,
            cooldown: 2.5f
        ));

        registry.RegisterAction(new PlayerAction(
            "drink_potion",
            "Drink Potion",
            PlayerState.DrinkingPotion,
            "Use_Item",
            duration: 0.8f,
            cooldown: 0f
        ));

        registry.RegisterAction(new PlayerAction(
            "cast_spell",
            "Cast Spell",
            PlayerState.CastingSpell,
            "Spellcasting",
            duration: 0.8f,
            cooldown: 1.5f
        ));

        return registry;
    }
}
