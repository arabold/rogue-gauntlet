using Godot;

/// <summary>
/// Combat diagnostics: attack hit volumes and helper meshes, and the combat and AI logs. Each toggle applies at once, to
/// existing and later actors.
/// </summary>
[GlobalClass]
public partial class CombatDisplayTool : DebugTool
{
	private const int DebugMeshesId = 0;
	private const int CombatLogsId = 1;
	private const int AiLogsId = 2;

	/// <summary>Shows attack hit volumes and every node in the <c>debug_mesh</c> group.</summary>
	public bool DebugMeshesEnabled
	{
		get;
		set
		{
			field = value;
			AttackController.GlobalDebugDrawEnabled = value;
			if (!IsInsideTree()) { return; }
			foreach (Node node in GetTree().GetNodesInGroup("attack_controller"))
			{
				if (node is AttackController attackController) { attackController.DebugDrawEnabled = value; }
			}
			foreach (Node node in GetTree().GetNodesInGroup("debug_mesh"))
			{
				if (node is Node3D node3D) { node3D.Visible = value; }
			}
		}
	}

	public bool CombatLogsEnabled
	{
		get => GameDebug.CombatLogsEnabled;
		set => GameDebug.CombatLogsEnabled = value;
	}

	public bool AiLogsEnabled
	{
		get => GameDebug.AiLogsEnabled;
		set => GameDebug.AiLogsEnabled = value;
	}

	public override void BuildMenu(PopupMenu menu)
	{
		menu.AddCheckItem("Debug meshes", DebugMeshesId);
		menu.AddCheckItem("Combat logs", CombatLogsId);
		menu.AddCheckItem("AI logs", AiLogsId);
		menu.IdPressed += OnPressed;
		// Hit volumes start hidden; the global switch would otherwise carry over from an earlier scene.
		DebugMeshesEnabled = false;
		RefreshMenu(menu);
	}

	public override void RefreshMenu(PopupMenu menu)
	{
		SetChecked(menu, DebugMeshesId, DebugMeshesEnabled);
		SetChecked(menu, CombatLogsId, CombatLogsEnabled);
		SetChecked(menu, AiLogsId, AiLogsEnabled);
	}

	private void OnPressed(long id)
	{
		switch (id)
		{
			case DebugMeshesId: DebugMeshesEnabled = !DebugMeshesEnabled; break;
			case CombatLogsId: CombatLogsEnabled = !CombatLogsEnabled; break;
			case AiLogsId: AiLogsEnabled = !AiLogsEnabled; break;
		}
		RefreshMenu(Popup);
	}
}
