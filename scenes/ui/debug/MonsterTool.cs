using Godot;

/// <summary>Actions on the monsters in the scene.</summary>
[GlobalClass]
public partial class MonsterTool : DebugTool
{
	private const int KillAllId = 0;

	public override void BuildMenu(PopupMenu menu)
	{
		menu.AddItem("Kill all enemies", KillAllId);
		menu.IdPressed += OnPressed;
	}

	/// <summary>Kills every living enemy through its health, as lethal damage would.</summary>
	public void KillAll()
	{
		foreach (Node enemy in GetTree().GetNodesInGroup("enemy"))
		{
			HealthComponent health = enemy.GetNodeOrNull<HealthComponent>("HealthComponent");
			if (health != null && !health.IsDead)
			{
				health.TakeDamage(health.CurrentHealth);
			}
		}
	}

	private void OnPressed(long id)
	{
		if (id == KillAllId)
		{
			KillAll();
		}
	}
}
