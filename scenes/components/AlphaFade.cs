using Godot;
using System;

/// <summary>
/// Fades a floating visual in and out through alpha and hides it once fully faded out, so a hidden visual costs nothing
/// to draw. Alpha works on the Mobile renderer, unlike <see cref="GeometryInstance3D.Transparency"/>. The owner applies
/// the alpha to whatever it fades (a label's text and outline, a sprite's modulate), keeping its own colors.
/// </summary>
public sealed class AlphaFade
{
	private readonly Node3D _target;
	private readonly Action<float> _apply;
	private readonly Callable _applyCallable;
	private readonly Callable _hideCallable;
	private Tween _tween;
	private float _alpha;

	/// <summary>True while the visual is showing or fading in.</summary>
	public bool IsShown { get; private set; }

	/// <summary>Starts fully faded out and hidden.</summary>
	public AlphaFade(Node3D target, Action<float> apply)
	{
		_target = target;
		_apply = apply;
		_applyCallable = Callable.From<float>(Apply);
		_hideCallable = Callable.From(() => _target.Visible = false);
		Apply(0f);
		target.Visible = false;
	}

	/// <summary>
	/// Fades toward shown or hidden over <paramref name="duration"/> for a full fade; reversing mid-fade continues from
	/// the current alpha. Returns whether the state changed.
	/// </summary>
	public bool Set(bool shown, float duration)
	{
		if (IsShown == shown) return false;
		IsShown = shown;
		_tween?.Kill();
		float target = shown ? 1f : 0f;
		_target.Visible = true;
		_tween = _target.CreateTween();
		_tween.TweenMethod(_applyCallable, _alpha, target, duration * Mathf.Abs(target - _alpha));
		if (!shown) _tween.TweenCallback(_hideCallable);
		return true;
	}

	private void Apply(float alpha)
	{
		_alpha = alpha;
		_apply(alpha);
	}
}