using Godot;
using System;
using System.Collections.Generic;

/// <summary>Visual diagnostics shared by gameplay and debug worlds.</summary>
[GlobalClass]
public partial class VisualEffectsTool : DebugTool
{
	private int _ssaoId, _blurId;
	private readonly List<int> _effectIds = [];

	protected override void BuildMenu(PopupMenu menu)
	{
		AddToggle(menu, "Door x-ray", () => GameDebug.DoorXrayEnabled, value => SetXray(() => GameDebug.DoorXrayEnabled = value));
		AddToggle(menu, "Monster x-ray", () => GameDebug.MonsterXrayEnabled, value => SetXray(() => GameDebug.MonsterXrayEnabled = value));
		AddToggle(menu, "Loot x-ray", () => GameDebug.LootXrayEnabled, value => SetXray(() => GameDebug.LootXrayEnabled = value));
		menu.AddSeparator();
		AddEffectToggle(menu, "Atmospheric fog", EnvironmentalEffect.Fog);
		_ssaoId = AddToggle(menu, "Ambient occlusion", () => Menu?.DebugWorld?.Lighting?.Environment?.SsaoEnabled == true,
			value => { if (Menu?.DebugWorld?.Lighting?.Environment is { } environment) environment.SsaoEnabled = value; });
		_blurId = AddToggle(menu, "Focus blur", () => Menu?.FocusBlur?.Visible == true,
			value => { if (Menu?.FocusBlur is { } blur) blur.Visible = value; });
		menu.AddSeparator();
		AddEffectToggle(menu, "Surface marks (wet spots, soot)", EnvironmentalEffect.SurfaceMarks);
		AddEffectToggle(menu, "Drips and splashes", EnvironmentalEffect.Drips);
		AddEffectToggle(menu, "Dust and glimmer", EnvironmentalEffect.Dust);
		AddEffectToggle(menu, "Wind (banners and plants)", EnvironmentalEffect.Wind);
		AddEffectToggle(menu, "Reflections", EnvironmentalEffect.Reflections);
	}

	private void AddEffectToggle(PopupMenu menu, string label, EnvironmentalEffect effect)
	{
		int id = AddToggle(menu, label, () => Menu?.DebugWorld?.Lighting?.IsEffectEnabled(effect) == true,
			value =>
			{
				if (Menu?.DebugWorld?.Lighting is not { } lighting) return;
				lighting.DisabledEffects = value ? lighting.DisabledEffects & ~effect : lighting.DisabledEffects | effect;
			});
		_effectIds.Add(id);
	}

	protected override void OnRefresh()
	{
		foreach (int id in _effectIds) SetDisabled(Popup, id, Menu?.DebugWorld?.Lighting == null);
		SetDisabled(Popup, _ssaoId, Menu?.DebugWorld?.Lighting?.Environment == null);
		SetDisabled(Popup, _blurId, Menu?.FocusBlur == null);
	}

	private static void SetXray(Action change)
	{
		change();
		SignalBus.EmitXraySettingsChanged();
	}
}