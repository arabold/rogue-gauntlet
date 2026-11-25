using System.Collections.Generic;
using Godot;

/// <summary>Owns the selected effects' floor lifetime and darkness gate, independently of their rendering implementation.</summary>
[Tool] // Exported by the tool Level script: a tool script's exports must be tool types in the editor.
[GlobalClass]
public partial class DungeonAtmosphere : Node3D
{
	[Export] public WorldLighting Lighting { get; set; }

	private readonly List<AtmosphereEffect> _effects = [];
	private AtmosphereContext _context;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		Visible = false;
		if (Lighting == null)
		{
			GD.PushError($"{Name}: assign the world's Lighting.");
			return;
		}
		this.SubscribeUntilExit(Lighting, source => source.DarknessChanged += ApplyDarkness, source => source.DarknessChanged -= ApplyDarkness);
		this.SubscribeUntilExit(Lighting, source => source.EffectsChanged += ApplyEffects, source => source.EffectsChanged -= ApplyEffects);
	}

	/// <summary>Releases effects before their shared floor textures, including when generation fails or the world exits.</summary>
	public void Clear()
	{
		foreach (var effect in _effects)
		{
			effect.Clear();
			effect.Free();
		}
		_effects.Clear();
		_context?.Dispose();
		_context = null;
		Visible = false;
	}

	/// <summary>Instantiates the selected scenes after generation and exploration restoration. No preset is a valid clear-air choice.</summary>
	public void Initialize(GeneratedFloor floor, AtmosphereProfile profile, ulong seed)
	{
		Clear();
		if (floor == null || Lighting == null || profile?.Effects is not { Length: > 0 }) return;
		_context = new(floor, seed);
		if (_context.TileCount == 0) { Clear(); return; }
		foreach (var scene in profile.Effects)
		{
			if (scene == null) { GD.PushWarning($"{Name}: an atmosphere effect scene is unassigned."); continue; }
			var instance = scene.Instantiate();
			if (instance is not AtmosphereEffect effect)
			{
				GD.PushError($"{Name}: {scene.ResourcePath} must have an AtmosphereEffect root.");
				instance.Free();
				continue;
			}
			AddChild(effect);
			effect.Initialize(_context);
			_effects.Add(effect);
		}
		ApplyEffects();
	}

	private void ApplyDarkness(bool isDark) => ApplyEffects();

	private void ApplyEffects()
	{
		Visible = _effects.Count > 0;
		foreach (var effect in _effects)
			effect.SetActive(Visible && Lighting.IsEffectEnabled(effect.Category), Lighting.IsDark);
	}

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint()) Clear();
	}
}