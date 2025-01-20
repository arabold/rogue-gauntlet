using System;
using Godot;

[GlobalClass]
public abstract partial class MobFactoryStrategy : Resource
{
	public abstract PackedScene CreateEnemy(int level);
}
