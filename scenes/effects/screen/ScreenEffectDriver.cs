using Godot;
using System;

/// <summary>
/// Shared base for runtime drivers that bind a gameplay source to a <see cref="ScreenEffect"/>.
/// Owns the target <see cref="Effect"/> export, tree-lifetime subscription cleanup, and binding on tree entry.
/// </summary>
public abstract partial class ScreenEffectDriver : Node
{
	private Action _unsubscribe = () => { };

	/// <summary>Effect to drive. Required.</summary>
	[Export] public ScreenEffect Effect { get; set; }

	public override void _Ready()
	{
		if (Effect == null)
		{
			GD.PushError($"{Name}: {GetType().Name} requires an {nameof(Effect)}.");
		}
	}

	public override void _EnterTree() => Bind();

	/// <summary>Re-subscribes to the current source and synchronizes the effect state.</summary>
	protected abstract void Bind();

	/// <summary>Replaces the active source subscription using <see cref="NodeExtensions.SubscribeUntilExit{T}"/>.</summary>
	protected void Rebind<TSource>(TSource source, Action<TSource> subscribe, Action<TSource> unsubscribe)
		where TSource : class
	{
		_unsubscribe();
		_unsubscribe = this.SubscribeUntilExit(source, subscribe, unsubscribe);
	}

	/// <summary>Updates a source reference and rebinds immediately when already inside the scene tree.</summary>
	protected void SetSource<TSource>(ref TSource field, TSource value)
		where TSource : class
	{
		if (ReferenceEquals(field, value))
		{
			return;
		}

		field = value;
		if (IsInsideTree())
		{
			Bind();
		}
	}
}