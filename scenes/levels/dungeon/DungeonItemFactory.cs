using Godot;
using Godot.Collections;
using System;
using System.Linq;

[GlobalClass]
public partial class WeightedItem : Resource
{
    public Item Item;
    public float Weight;
}

public partial class DungeonItemFactory : ItemFactory
{
    [Export] public Array<WeightedItem> Weapons;
    [Export] public Array<WeightedItem> Armors;
    [Export] public Array<WeightedItem> Consumables;

    public override Weapon CreateWeapon(int dungeonDepth)
    {
        if (Weapons == null || Weapons.Count == 0)
        {
            return null;
        }

        float[] weights = Weapons.Select(w => w.Weight).ToArray();

        var random = new RandomNumberGenerator();
        random.Seed = GD.Randi();
        int randomIndex = (int)random.RandWeighted(weights);

        return Weapons[randomIndex].Item as Weapon;
    }

    public override Armor CreateArmor(int dungeonDepth)
    {
        if (Armors == null || Armors.Count == 0)
        {
            return null;
        }

        float[] weights = Armors.Select(w => w.Weight).ToArray();

        var random = new RandomNumberGenerator();
        random.Seed = GD.Randi();
        int randomIndex = (int)random.RandWeighted(weights);

        return Armors[randomIndex].Item as Armor;
    }

    public override Item CreateConsumable(int dungeonDepth)
    {
        if (Consumables == null || Consumables.Count == 0)
        {
            return null;
        }

        float[] weights = Consumables.Select(w => w.Weight).ToArray();

        var random = new RandomNumberGenerator();
        random.Seed = GD.Randi();
        int randomIndex = (int)random.RandWeighted(weights);

        return Consumables[randomIndex].Item;
    }
}
