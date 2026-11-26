using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Zber surovin (P1-09).
///
/// Baa-ricade > Nastavit zber surovin
///     Prefabom Tree, Rock, GrassTuft a BerryBush prida komponent Gatherable (co davaju, ako dlho
///     sa zbiera, ci dorastaju) a hracovi PlayerInteractor + InteractionUI. Spusti aj nastavenie inventara.
///     Da sa spustit viackrat; hodnoty upravene v Inspectore neprepise.
///
/// Grafika "po vyzbierani" je druhy snimok v .aseprite (tree: stump, berry_bush: empty).
/// Kym nie je nakresleny, pouzije sa placeholder. Baa-ricade > Aktualizovat sprity z Aseprite
/// ho po nakresleni napoji (rovnako ako plny sprite).
/// </summary>
public static partial class PlaceholderMapBuilder
{
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    class GatherDef
    {
        public string Prefab, Item, Action;
        public int Min, Max;
        public float Time;
        public int DepletedFrame = -1;  // -1 = po vyzbierani zmizne
        public Func<Px> DepletedPaint;
        public bool BlockWhenDepleted;
        public int RegrowDays;
    }

    // Startovne hodnoty, doladia sa pri testovani (aj priamo v Inspectore na prefabe).
    static readonly GatherDef[] Gatherables =
    {
        new GatherDef { Prefab = "Tree", Item = "wood", Action = "Chop tree", Min = 3, Max = 4, Time = 2.0f,
                        DepletedFrame = 1, DepletedPaint = Px.Stump, BlockWhenDepleted = true, RegrowDays = 3 },
        new GatherDef { Prefab = "Rock", Item = "stone", Action = "Mine rock", Min = 2, Max = 3, Time = 1.6f, RegrowDays = 4 },
        new GatherDef { Prefab = "GrassTuft", Item = "fiber", Action = "Cut grass", Min = 1, Max = 2, Time = 0.6f, RegrowDays = 1 },
        new GatherDef { Prefab = "BerryBush", Item = "berries", Action = "Pick berries", Min = 2, Max = 3, Time = 0.8f,
                        DepletedFrame = 1, DepletedPaint = Px.BerryBushEmpty, BlockWhenDepleted = true, RegrowDays = 2 },
        // Explore: huby v lese, srot v ruinach (vzacny, daleko od farmy, dlho sa obnovuje)
        new GatherDef { Prefab = "Mushroom", Item = "mushroom", Action = "Pick mushrooms", Min = 1, Max = 2, Time = 0.5f, RegrowDays = 2 },
        new GatherDef { Prefab = "ScrapPile", Item = "scrap", Action = "Search scrap", Min = 1, Max = 2, Time = 2.5f, RegrowDays = 5 },
    };

    static Dictionary<string, ItemData> gatherItems;

    [MenuItem("Baa-ricade/Nastavit zber surovin")]
    public static void SetupGathering()
    {
        InventorySetup.Setup();
        gatherItems = null;
        UpdatePrefabs(); // prida/aktualizuje Gatherable na prefaboch surovin
        SetupPlayerInteraction();
        AssetDatabase.SaveAssets();
        Debug.Log("Zber surovin nastaveny: Gatherable na Tree, Rock, GrassTuft, BerryBush; PlayerInteractor + InteractionUI na hracovi.");
    }

    /// Vola sa z UpdatePrefabs pre kazdy prefab. Novy Gatherable nastavi cely,
    /// existujucemu zmeni len depleted sprite (aby sa napojil nakresleny snimok).
    static void ApplyGathering(GameObject root, PrefabDef def)
    {
        var g = Gatherables.FirstOrDefault(x => x.Prefab == def.Name);
        if (g == null) return;

        if (gatherItems == null || gatherItems.Values.Any(v => v == null))
            gatherItems = InventorySetup.EnsureItems();

        var gatherable = root.GetComponent<Gatherable>();
        bool isNew = gatherable == null;
        if (isNew) gatherable = root.AddComponent<Gatherable>();

        var so = new SerializedObject(gatherable);
        var itemProp = so.FindProperty("item");
        if (isNew)
        {
            so.FindProperty("amount").vector2IntValue = new Vector2Int(g.Min, g.Max);
            so.FindProperty("actionName").stringValue = g.Action;
            so.FindProperty("gatherTime").floatValue = g.Time;
            so.FindProperty("blockWhenDepleted").boolValue = g.BlockWhenDepleted;
            so.FindProperty("regrowDays").intValue = g.RegrowDays;
        }
        if (itemProp.objectReferenceValue == null && gatherItems.TryGetValue(g.Item, out var item))
            itemProp.objectReferenceValue = item;

        if (g.DepletedFrame >= 0)
            so.FindProperty("depletedSprite").objectReferenceValue =
                ResolveSprite(def.Aseprite, g.DepletedFrame, $"Objects/{def.Name}_Depleted", g.DepletedPaint);

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetupPlayerInteraction()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
        {
            Debug.LogWarning($"{PlayerPrefabPath} nenajdeny, hrac nedostal PlayerInteractor.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (root.GetComponent<PlayerInteractor>() == null) root.AddComponent<PlayerInteractor>();
            if (root.GetComponent<InteractionUI>() == null) root.AddComponent<InteractionUI>();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
