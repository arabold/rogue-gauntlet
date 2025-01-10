using Godot;

public partial class CastSpellAction : ActionBase
{
    public override string Id => "cast_spell";
    public override float PerformDuration => 0.5f;
    public override float CooldownDuration => 0.0f;

    public CastSpellAction()
    {
    }

    public override void Execute(Player player)
    {
        GD.Print($"{player.Name} casting spell!");
    }
}
