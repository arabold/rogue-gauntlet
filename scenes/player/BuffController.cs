using Godot;
using Godot.Collections;
using System.Linq;

/// <summary>
/// Owns runtime buff instances applied to a player.
/// </summary>
public partial class BuffController : Node
{
	[Export] public Player Player { get; set; }

	public Array<ActiveBuff> ActiveBuffs { get; } = new();

	public override void _Ready()
	{
		Player ??= GetOwner<Player>();
	}

	public override void _PhysicsProcess(double delta)
	{
		for (int i = ActiveBuffs.Count - 1; i >= 0; i--)
		{
			if (ActiveBuffs[i].IsExpired)
				RemoveActiveBuff(ActiveBuffs[i]);
		}
	}

	public void ApplyBuff(Buff buff)
	{
		GD.Print($"{Player.Name} applied buff {buff.Name}");
		var activeBuff = new ActiveBuff();
		activeBuff.Initialize(Player, buff);
		ActiveBuffs.Add(activeBuff);

		AddChild(activeBuff);
	}

	public void RemoveBuff(Buff buff)
	{
		var activeBuff = ActiveBuffs.FirstOrDefault(b => b.Buff == buff);
		if (activeBuff != null)
		{
			GD.Print($"{Player.Name} removed buff {buff.Name}");
			RemoveActiveBuff(activeBuff);
		}
	}

	private void RemoveActiveBuff(ActiveBuff activeBuff)
	{
		activeBuff.Deactivate();
		ActiveBuffs.Remove(activeBuff);
		RemoveChild(activeBuff);
		activeBuff.QueueFree();
	}
}
