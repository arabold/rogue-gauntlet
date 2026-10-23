using Godot;

public interface ICombatAttackAction : IPlayerAction
{
	bool TryPrepare(Player player, out PreparedAttack attack);
	bool StartPrepared(Player player, PreparedAttack attack, ActorActionLease lease = default);
	string CooldownKey { get; }
}