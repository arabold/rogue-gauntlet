using System;
using Godot;

[GlobalClass]
public abstract partial class MobFactory : Resource
{
	public abstract PackedScene CreateEnemy(uint dungeonDepth, RandomNumberGenerator rng);
	public virtual void Reset() { }
}