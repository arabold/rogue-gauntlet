using Godot;
using System;

/// <summary>Visual diagnostics shared by gameplay and debug worlds.</summary>
[GlobalClass]
public partial class VisualEffectsTool : DebugTool
{
	private int _fogId, _ssaoId, _blurId;

	protected override void BuildMenu(PopupMenu menu)
	{
		AddToggle(menu, "Door x-ray", () => GameDebug.DoorXrayEnabled, value => SetXray(() => GameDebug.DoorXrayEnabled = value));
		AddToggle(menu, "Monster x-ray", () => GameDebug.MonsterXrayEnabled, value => SetXray(() => GameDebug.MonsterXrayEnabled = value));
		AddToggle(menu, "Loot x-ray", () => GameDebug.LootXrayEnabled, value => SetXray(() => GameDebug.LootXrayEnabled = value));
		menu.AddSeparator();
		_fogId = AddToggle(menu, "Atmospheric fog", () => Menu?.DebugWorld?.Lighting?.FogEnabled == true,
			value => { if (Menu?.DebugWorld?.Lighting is { } lighting) lighting.FogEnabled = value; });
		_ssaoId = AddToggle(menu, "Ambient occlusion", () => Menu?.DebugWorld?.Lighting?.Environment?.SsaoEnabled == true,
			value => { if (Menu?.DebugWorld?.Lighting?.Environment is { } environment) environment.SsaoEnabled = value; });
		_blurId = AddToggle(menu, "Focus blur", () => Menu?.FocusBlur?.Visible == true,
			value => { if (Menu?.FocusBlur is { } blur) blur.Visible = value; });
	}

	protected override void OnRefresh()
	{
		SetDisabled(Popup, _fogId, Menu?.DebugWorld?.Lighting?.Environment == null);
		SetDisabled(Popup, _ssaoId, Menu?.DebugWorld?.Lighting?.Environment == null);
		SetDisabled(Popup, _blurId, Menu?.FocusBlur == null);
	}

	private static void SetXray(Action change)
	{
		change();
		SignalBus.EmitXraySettingsChanged();
	}
}