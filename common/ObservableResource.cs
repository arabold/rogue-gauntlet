
using Godot;
using System.Collections.Generic;

/// <summary>
/// Base class for all observable resources.
///
/// This class provides a SetValue method that will update the value of a field 
/// and emit a changed signal if the value has changed.
/// </summary>
[GlobalClass]
public abstract partial class ObservableResource : Resource
{
    public virtual void SetValue<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            EmitChanged();
        }
    }
}