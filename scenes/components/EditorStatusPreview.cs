using Godot;

/// <summary>Previews authored actor status surfaces in the editor without running gameplay.</summary>
[Tool]
public partial class EditorStatusPreview : Node
{
	[Export] public Node Actor { get; set; }
	[Export] public Node3D Target { get; set; }
	private readonly EditorSurfacePreview _preview = new();
	private readonly record struct Binding(Node Component, StatusCondition Condition, WorldEffectDefinition Effect, bool Active);
	private readonly Binding[] _previous = new Binding[4];
	private Node3D _burnPreview;
	private WorldEffectDefinition _burnDefinition;
	private Binding _previousBurn;
	private bool _dirty = true;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint()) { SetProcess(false); return; }
		if (Actor == null || Target == null) { GD.PushWarning($"{Name}: assign the preview actor and visual root."); SetProcess(false); return; }
		this.SubscribeUntilExit(GetTree(), t => t.NodeAdded += OnNodeAdded, t => t.NodeAdded -= OnNodeAdded);
		this.SubscribeUntilExit(GetTree(), t => t.NodeRemoved += OnNodeRemoved, t => t.NodeRemoved -= OnNodeRemoved);
	}

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint() || !IsInstanceValid(Actor) || !IsInstanceValid(Target)) return;
		var invisible = ReadBinding("Invisible", "Invisibility", "Effect");
		var frozen = ReadBinding("Frozen", "FrozenStatus", "Appearance");
		var stone = ReadBinding("StoneSkin", "StoneSkinStatus", "Effect");
		var iron = ReadBinding("IronSkin", "IronSkinStatus", "Effect");
		var burning = ReadBinding("Burning", "BurningStatus", "Appearance");
		if (!_dirty && invisible == _previous[0] && frozen == _previous[1] && stone == _previous[2] && iron == _previous[3] && burning == _previousBurn) return;
		_previous[0] = invisible; _previous[1] = frozen; _previous[2] = stone; _previous[3] = iron; _dirty = false;
		_previousBurn = burning;
		SyncBurnPreview(invisible.Active && burning.Effect?.SuppressWhileInvisible == true ? burning with { Active = false } : burning);
		WorldEffectDefinition effect = null; SurfaceAppearanceProfile profile = null;
		foreach (var binding in _previous)
		{
			var candidate = binding.Effect;
			if (!binding.Active || candidate == null) continue;
			var surface = SurfaceFor(candidate);
			if (surface == null) { GD.PushWarning($"{Name}: {candidate.Id} requires an authored surface profile."); continue; }
			if (SurfaceMaterialFactory.IsHigherPriority(surface, candidate.Id, profile, effect?.Id)) { effect = candidate; profile = surface; }
		}
		if (!_preview.Set(Target, profile)) GD.PushWarning($"{Name}: cannot preview the selected actor surface.");
	}

	private void SyncBurnPreview(Binding binding)
	{
		var definition = binding.Active ? binding.Effect : null;
		if (definition == _burnDefinition && (definition == null || IsInstanceValid(_burnPreview))) return;
		if (IsInstanceValid(_burnPreview)) _burnPreview.QueueFree();
		_burnPreview = null; _burnDefinition = definition;
		if (definition?.Scene?.Instantiate() is not Node3D preview) return;
		_burnPreview = preview; VisualHelper.Mark(preview); Target.AddChild(preview);
	}

	private Binding ReadBinding(string flag, string componentProperty, string effectProperty)
	{
		var value = Actor.Get(componentProperty);
		var component = value.VariantType == Variant.Type.Object ? value.AsGodotObject() as Node : null;
		if (component == null) return default;
		var conditionValue = component.Get("Condition"); var effectValue = component.Get(effectProperty);
		var condition = conditionValue.VariantType == Variant.Type.Object ? conditionValue.AsGodotObject() as StatusCondition : null;
		var effect = effectValue.VariantType == Variant.Type.Object ? effectValue.AsGodotObject() as WorldEffectDefinition : null;
		return new Binding(component, condition, effect, condition != null && HasStatus(flag, condition));
	}

	private bool HasStatus(string property, StatusCondition condition)
	{
		if (Actor.Get(property).AsBool()) return true;
		var initial = Actor.Get("InitialConditions");
		if (condition == null || initial.VariantType != Variant.Type.Array) return false;
		foreach (var value in initial.AsGodotArray()) if (value.VariantType == Variant.Type.Object && value.AsGodotObject() == condition) return true;
		return false;
	}

	private static SurfaceAppearanceProfile SurfaceFor(WorldEffectDefinition effect)
	{
		if (effect.Scene == null) return null;
		var state = effect.Scene.GetState();
		if (state.GetNodeCount() == 0) return null;
		for (int i = 0; i < state.GetNodePropertyCount(0); i++)
			if (state.GetNodePropertyName(0, i) == "Surface") return state.GetNodePropertyValue(0, i).AsGodotObject() as SurfaceAppearanceProfile;
		return null;
	}

	private void OnNodeAdded(Node node) { if (IsInstanceValid(Target) && Target.IsAncestorOf(node)) _dirty = true; }
	private void OnNodeRemoved(Node node) { if (_preview.Contains(node)) _dirty = true; }
	public override void _ExitTree()
	{
		_preview.Clear();
		if (IsInstanceValid(_burnPreview)) _burnPreview.QueueFree();
	}
}
