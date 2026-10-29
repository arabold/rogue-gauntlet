using Godot;

/// <summary>Shared tab navigation. Add another authored preview scene here when the spell harness is ready.</summary>
public partial class EffectHarnessNavigation : CanvasLayer
{
	[Export] public TabBar Tabs { get; set; }
	[Export] public string[] Titles { get; set; } = [];
	[Export(PropertyHint.File, "*.tscn")] public string[] ScenePaths { get; set; } = [];
	[Export] public int SelectedPreview { get; set; }
	public override void _Ready()
	{
		if (Tabs == null || Titles.Length != ScenePaths.Length || SelectedPreview < 0 || SelectedPreview >= Titles.Length)
		{ GD.PushError("Effect harness navigation requires matching titles and scene paths."); return; }
		foreach (string title in Titles) Tabs.AddTab(title);
		Tabs.CurrentTab = SelectedPreview;
		Tabs.TabChanged += index => { if (index != SelectedPreview) GetTree().ChangeSceneToFile(ScenePaths[index]); };
	}
}