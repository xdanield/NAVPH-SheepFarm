using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inventar hraca. Sloty 0..HotbarSize-1 su hotbar (dole na obrazovke),
/// za nimi nasleduju riadky batohu (vidno ich po otvoreni inventara klavesom I).
/// Data a pravidla su tu, vykreslovanie a ovladanie mysou je v InventoryUI.
/// </summary>
public class Inventory : MonoBehaviour
{
    [SerializeField, Range(1, 10)] private int hotbarSize = 6;
    [Tooltip("Kolko riadkov ma batoh nad hotbarom (kazdy riadok ma tolko slotov ako hotbar).")]
    [SerializeField, Range(0, 6)] private int bagRows = 1;
    [Tooltip("Co ma hrac v inventari na zaciatku hry.")]
    [SerializeField] private List<ItemStack> startingItems = new List<ItemStack>();

    private ItemStack[] slots;
    private int selectedIndex;

    public int HotbarSize => hotbarSize;
    public int Columns => hotbarSize;
    public int BagRows => bagRows;
    public int SlotCount => slots.Length;
    public bool IsHotbar(int index) => index < hotbarSize;

    public int SelectedIndex => selectedIndex;
    public ItemStack SelectedStack => slots[selectedIndex];

    /// <summary>Zmenil sa obsah niektoreho slotu.</summary>
    public event Action Changed;
    /// <summary>Hrac si vybral iny slot v hotbare.</summary>
    public event Action<int> SelectionChanged;

    void Awake()
    {
        slots = new ItemStack[hotbarSize * (1 + bagRows)];
        foreach (var stack in startingItems)
        {
            if (stack.IsEmpty) continue;
            int left = Add(stack.item, stack.count);
            if (left > 0) Debug.LogWarning($"Inventar: {left}x {stack.item.DisplayName} sa nezmestilo.");
        }
    }

    // ------------------------------------------------------------ citanie

    public ItemStack Get(int index) => slots[index];

    public int CountOf(ItemData item)
    {
        int total = 0;
        foreach (var s in slots)
            if (s.item == item) total += s.count;
        return total;
    }

    public bool Has(ItemData item, int count = 1) => CountOf(item) >= count;

    /// <summary>Kolko kusov tohto predmetu sa este zmesti (doplnenie stackov + prazdne sloty).</summary>
    public int SpaceFor(ItemData item)
    {
        if (item == null) return 0;
        int space = 0;
        foreach (var s in slots)
        {
            if (s.IsEmpty) space += item.MaxStack;
            else if (s.item == item) space += s.SpaceLeft;
        }
        return space;
    }

    // ------------------------------------------------------------ pridavanie a odoberanie

    /// <summary>
    /// Prida predmety: najprv doplni existujuce stacky, potom prazdne sloty (hotbar ma prednost).
    /// Vrati, kolko sa nezmestilo (0 = vsetko sa zmestilo).
    /// </summary>
    public int Add(ItemData item, int count)
    {
        int left = AddToRange(item, count, 0, slots.Length);
        Changed?.Invoke();
        return left;
    }

    /// <summary>Odoberie predmety (napr. pri stavani). Ak ich nie je dost, neodoberie nic a vrati false.</summary>
    public bool Remove(ItemData item, int count)
    {
        if (!Has(item, count)) return false;
        // Od konca, aby sa najprv minali veci z batohu a hotbar zostal.
        for (int i = slots.Length - 1; i >= 0 && count > 0; i--)
        {
            if (slots[i].item != item) continue;
            int take = Mathf.Min(count, slots[i].count);
            slots[i].count -= take;
            count -= take;
            if (slots[i].count <= 0) slots[i] = ItemStack.Empty;
        }
        Changed?.Invoke();
        return true;
    }

    private int AddToRange(ItemData item, int count, int start, int end)
    {
        if (item == null || count <= 0) return 0;

        for (int i = start; i < end && count > 0; i++)
        {
            if (slots[i].item != item) continue;
            int move = Mathf.Min(count, slots[i].SpaceLeft);
            slots[i].count += move;
            count -= move;
        }
        for (int i = start; i < end && count > 0; i++)
        {
            if (!slots[i].IsEmpty) continue;
            int move = Mathf.Min(count, item.MaxStack);
            slots[i] = new ItemStack(item, move);
            count -= move;
        }
        return count;
    }

    // ------------------------------------------------------------ vyber v hotbare

    public void Select(int index)
    {
        index = Mathf.Clamp(index, 0, hotbarSize - 1);
        if (index == selectedIndex) return;
        selectedIndex = index;
        SelectionChanged?.Invoke(selectedIndex);
    }

    public void SelectNext(int direction)
    {
        Select((selectedIndex + direction + hotbarSize) % hotbarSize);
    }

    // ------------------------------------------------------------ klikanie v otvorenom inventari
    // "hand" je stack, ktory hrac drzi na kurzore. Metody vratia, co drzi po kliknuti.

    /// <summary>Lavy klik: zobrat cely stack / polozit / spojit rovnake / vymenit.</summary>
    public ItemStack PrimaryClick(int index, ItemStack hand)
    {
        var slot = slots[index];

        if (hand.IsEmpty)
        {
            if (slot.IsEmpty) return ItemStack.Empty;
            slots[index] = ItemStack.Empty;
            hand = slot;
        }
        else if (slot.IsEmpty)
        {
            slots[index] = hand;
            hand = ItemStack.Empty;
        }
        else if (slot.item == hand.item)
        {
            int move = Mathf.Min(hand.count, slot.SpaceLeft);
            slots[index].count += move;
            hand.count -= move;
            if (hand.count <= 0) hand = ItemStack.Empty;
        }
        else
        {
            slots[index] = hand;
            hand = slot;
        }

        Changed?.Invoke();
        return hand;
    }

    /// <summary>Pravy klik: s prazdnou rukou zoberie polovicu, s plnou polozi jeden kus.</summary>
    public ItemStack SecondaryClick(int index, ItemStack hand)
    {
        var slot = slots[index];

        if (hand.IsEmpty)
        {
            if (slot.IsEmpty) return ItemStack.Empty;
            int take = (slot.count + 1) / 2;
            hand = new ItemStack(slot.item, take);
            slots[index].count -= take;
            if (slots[index].count <= 0) slots[index] = ItemStack.Empty;
        }
        else if (slot.IsEmpty)
        {
            slots[index] = new ItemStack(hand.item, 1);
            hand.count--;
        }
        else if (slot.item == hand.item && slot.SpaceLeft > 0)
        {
            slots[index].count++;
            hand.count--;
        }
        else
        {
            return hand;
        }

        Changed?.Invoke();
        return hand.count > 0 ? hand : ItemStack.Empty;
    }

    /// <summary>Shift + klik: presunie stack medzi hotbarom a batohom.</summary>
    public void QuickMove(int index)
    {
        var stack = slots[index];
        if (stack.IsEmpty) return;

        bool fromHotbar = IsHotbar(index);
        int start = fromHotbar ? hotbarSize : 0;
        int end = fromHotbar ? slots.Length : hotbarSize;

        slots[index] = ItemStack.Empty;
        int left = AddToRange(stack.item, stack.count, start, end);
        if (left > 0) slots[index] = new ItemStack(stack.item, left);
        Changed?.Invoke();
    }

    /// <summary>Vrati stack z kurzora spat do inventara (pri zatvoreni). Vrati, co sa nezmestilo.</summary>
    public int Return(ItemStack hand)
    {
        return hand.IsEmpty ? 0 : Add(hand.item, hand.count);
    }
}
