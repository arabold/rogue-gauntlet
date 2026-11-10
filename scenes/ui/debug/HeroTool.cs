using Godot;

/// <summary>Actions on the player character.</summary>
[GlobalClass]
public partial class HeroTool : DebugTool
{
	private const int KillId = 0;

	public override void BuildMenu(PopupMenu menu)
	{
		menu.AddItem("Kill player", KillId);
		menu.IdPressed += OnPressed;
	}

	/// <summary>Kills the player through its health, as lethal damage would.</summary>
	public void Kill()
	{
		HealthComponent health = Menu?.Player?.HealthComponent;
		if (health != null && !health.IsDead)
		{
			health.TakeDamage(health.CurrentHealth);
		}
	}

	private void OnPressed(long id)
	{
		if (id == KillId)
		{
			Kill();
		}
	}
}
