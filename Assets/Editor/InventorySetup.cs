using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Baa-ricade > Nastavit inventar
///     1. Vytvori placeholder predmety (Assets/Items) a ich ikonky (Assets/Art/Placeholder/Items).
///        Existujuce subory neprepisuje, takze vlastne ikonky a upravy predmetov ostanu.
///     2. Na Player prefab prida Inventory + InventoryUI a ak je inventar prazdny,
///        da hracovi par testovacich predmetov na start.
/// Da sa spustit viackrat.
/// </summary>
public static class InventorySetup
{
    const string IconDir = "Assets/Art/Placeholder/Items";
    const string ItemDir = "Assets/Items";
    const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
    const int IconSize = 16;

    class ItemDef
    {
        public string File, Name, Description;
        public int MaxStack, StartCount;
        public Action<Icon> Paint;
    }

    static readonly ItemDef[] Items =
    {
        new ItemDef { File = "wood", Name = "Wood", Description = "For fences, spikes and traps.", MaxStack = 99, StartCount = 12, Paint = Icon.Wood },
        new ItemDef { File = "stone", Name = "Stone", Description = "For spikes.", MaxStack = 99, StartCount = 6, Paint = Icon.Stone },
        new ItemDef { File = "fiber", Name = "Fiber", Description = "Gathered from tall grass.", MaxStack = 99, StartCount = 4, Paint = Icon.Fiber },
        new ItemDef { File = "berries", Name = "Berries", Description = "Cooking ingredient.", MaxStack = 20, StartCount = 3, Paint = Icon.Berries },
        new ItemDef { File = "mushroom", Name = "Mushroom", Description = "Cooking ingredient.", MaxStack = 20, StartCount = 0, Paint = Icon.Mushroom },
        new ItemDef { File = "scrap", Name = "Scrap Metal", Description = "Found in abandoned buildings.", MaxStack = 50, StartCount = 0, Paint = Icon.Scrap },
    };

    [MenuItem("Baa-ricade/Nastavit inventar")]
    public static void Setup()
    {
        EnsureFolder(IconDir);
        EnsureFolder(ItemDir);

        var items = new ItemData[Items.Length];
        for (int i = 0; i < Items.Length; i++)
            items[i] = CreateItem(Items[i], CreateIcon(Items[i]));
        AssetDatabase.SaveAssets();

        SetupPlayer(items);
        Debug.Log($"Inventar nastaveny: {Items.Length} predmetov v {ItemDir}, Inventory + InventoryUI na {PlayerPrefab}.");
    }

    static Sprite CreateIcon(ItemDef def)
    {
        string path = $"{IconDir}/{def.File}.png";
        if (!File.Exists(path))
        {
            var icon = new Icon(IconSize);
            def.Paint(icon);
            File.WriteAllBytes(path, icon.ToPng());
            // ArtImportSettings nastavi novy subor ako pixelart sprite.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static ItemData CreateItem(ItemDef def, Sprite icon)
    {
        string path = $"{ItemDir}/{def.Name.Replace(" ", "")}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        bool isNew = item == null;
        if (isNew)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, path);
        }

        var so = new SerializedObject(item);
        if (isNew)
        {
            so.FindProperty("displayName").stringValue = def.Name;
            so.FindProperty("description").stringValue = def.Description;
            so.FindProperty("maxStack").intValue = def.MaxStack;
        }
        var iconProp = so.FindProperty("icon");
        if (iconProp.objectReferenceValue == null) iconProp.objectReferenceValue = icon;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(item);
        return item;
    }

    static void SetupPlayer(ItemData[] items)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab) == null)
        {
            Debug.LogWarning($"{PlayerPrefab} nenajdeny, inventar sa na hraca nepridal.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var inventory = root.GetComponent<Inventory>();
            if (inventory == null) inventory = root.AddComponent<Inventory>();
            if (root.GetComponent<InventoryUI>() == null) root.AddComponent<InventoryUI>();

            var so = new SerializedObject(inventory);
            var start = so.FindProperty("startingItems");
            if (start.arraySize == 0)
            {
                for (int i = 0; i < Items.Length; i++)
                {
                    if (Items[i].StartCount <= 0) continue;
                    int n = start.arraySize;
                    start.InsertArrayElementAtIndex(n);
                    var element = start.GetArrayElementAtIndex(n);
                    element.FindPropertyRelative("item").objectReferenceValue = items[i];
                    element.FindPropertyRelative("count").intValue = Items[i].StartCount;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ---------------------------------------------------------------- placeholder ikonky 16x16

    class Icon
    {
        readonly int size;
        readonly Color32[] px;

        public Icon(int size)
        {
            this.size = size;
            px = new Color32[size * size];
        }

        static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        // y = 0 je hore, aby sa kreslilo ako v Aseprite
        void Set(int x, int y, string hex)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            px[(size - 1 - y) * size + x] = Hex(hex);
        }

        void Rect(int x0, int y0, int x1, int y1, string hex)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    Set(x, y, hex);
        }

        void Disc(float cx, float cy, float r, string hex)
        {
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r)
                        Set(x, y, hex);
        }

        public byte[] ToPng()
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return png;
        }

        public static void Wood(Icon i)
        {
            // dve polena na sebe
            foreach (int y in new[] { 4, 9 })
            {
                i.Rect(2, y, 12, y + 3, "8a5a32");
                i.Rect(2, y, 12, y, "a8743f");
                i.Rect(2, y + 3, 12, y + 3, "5e3b20");
                i.Disc(13f, y + 2f, 2.2f, "d9b27a");
                i.Set(13, y + 1, "a8743f");
            }
        }

        public static void Stone(Icon i)
        {
            i.Disc(8f, 9.5f, 5.5f, "6f7480");
            i.Disc(7f, 8.5f, 4.5f, "8c929e");
            i.Disc(6f, 7f, 2f, "b3b8c2");
            i.Rect(3, 14, 12, 14, "4b4f58");
        }

        public static void Fiber(Icon i)
        {
            string[] greens = { "4f8a3a", "6aa84f", "8cc265" };
            for (int s = 0; s < 4; s++)
            {
                int x = 4 + s * 2;
                for (int y = 2; y < 14; y++)
                    i.Set(x + ((y / 3 + s) % 2), y, greens[s % 3]);
            }
            i.Rect(3, 9, 12, 10, "c9a86a"); // povrazok okolo zvazku
        }

        public static void Berries(Icon i)
        {
            i.Rect(7, 2, 8, 4, "4f8a3a");
            i.Rect(9, 2, 11, 3, "6aa84f");
            i.Disc(5.5f, 8.5f, 3f, "a3253a");
            i.Disc(10.5f, 8.5f, 3f, "c2304a");
            i.Disc(8f, 12f, 3f, "d13b55");
            i.Set(5, 7, "f08a9b");
            i.Set(10, 7, "f08a9b");
            i.Set(7, 11, "f08a9b");
        }

        public static void Mushroom(Icon i)
        {
            i.Rect(6, 8, 9, 14, "e8dcc0");
            i.Rect(9, 8, 9, 14, "c7b896");
            i.Disc(8f, 7f, 6f, "b8322a");
            i.Rect(1, 7, 14, 15, "00000000");
            i.Rect(6, 7, 9, 14, "e8dcc0");
            i.Rect(9, 7, 9, 14, "c7b896");
            i.Set(5, 3, "f2eadb");
            i.Set(10, 4, "f2eadb");
            i.Set(7, 5, "f2eadb");
        }

        public static void Scrap(Icon i)
        {
            i.Rect(2, 4, 13, 12, "7a7f87");
            i.Rect(2, 4, 13, 4, "a2a8b0");
            i.Rect(2, 12, 13, 12, "50545b");
            i.Rect(9, 9, 12, 11, "9c5a2e"); // hrdza
            i.Set(4, 6, "3a3d42");
            i.Set(11, 6, "3a3d42");
            i.Set(4, 10, "3a3d42");
        }
    }
}
