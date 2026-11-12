using System;

/// <summary>Predmet a jeho pocet v jednom slote. Prazdny slot = default.</summary>
[Serializable]
public struct ItemStack
{
    public ItemData item;
    public int count;

    public ItemStack(ItemData item, int count)
    {
        this.item = item;
        this.count = count;
    }

    public bool IsEmpty => item == null || count <= 0;
    public int SpaceLeft => IsEmpty ? 0 : item.MaxStack - count;

    public static ItemStack Empty => default;
}
