using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Druha lokacia: "The Wilds" (Explore) - vacsia uzavreta oblast nad farmou, kam vedie cesta cez utes.
///
/// Baa-ricade > Vygenerovat Explore lokaciu
///     Spusti Tools/mapgen/explore_map.py (teren, les, jazero, lom, ruiny -> Assets/Art/Map/explore_map.json
///     a explore_ground.png), postavi lokaciu nanovo a ulozi ju ako prefab Assets/Prefabs/Map/Explore.prefab
///     (v scene je len instancia). Farmu nemeni. Vychody hladaju ciel podla mena lokacie (LocationExit),
///     takze farma a Explore na seba nepotrebuju priame odkazy.
///
/// Explore je v tej istej scene ako farma, len posunute (ExploreOrigin), takze inventar, cas dna,
/// dorastanie surovin aj zombie bezia dalej. Hrac sa presuva cez LocationExit (stmavnutie obrazovky)
/// a kamera sa drzi v hraniciach aktualnej Location.
///
/// Konstanty nizsie cita aj Python generator (podla mena), nemen ich format.
///
///    ################ utes ################
///    TT  les (drevo, huby)  jazero   ruiny TT
///    TT  cistina (bobule)     |      srot  TT
///    TT          \____ krizovatka ___ lom  TT
///    TT  luka (vlakno)        |    kamene  TT
///    TTTTTTTTTTTTTTTTTTT cesta TTTTTTTTTTTTTT
///                         v na farmu (FarmExit)
/// </summary>
public static partial class PlaceholderMapBuilder
{
    public const int ExploreWidth = 96;
    public const int ExploreHeight = 64;
    const int ExploreCliffRows = 2;
    const int ExploreEntryX = 48;      // stlpec, kde cesta z farmy vchadza zdola do mapy
    const int ExploreSeed = 777;
    // Lavy dolny roh Explore vo svete. Daleko nad farmou (ta aj s pozadim konci na y ~ 42),
    // aby kamera nikdy nevidela z jednej lokacie do druhej.
    static readonly Vector2 ExploreOrigin = new Vector2(0, 64);

    const string ExploreJsonPath = "Assets/Art/Map/explore_map.json";
    const string ExploreGeneratorScript = "Tools/mapgen/explore_map.py";
    const string FarmName = "Farm", ExploreName = "The Wilds";

    [MenuItem("Baa-ricade/Vygenerovat Explore lokaciu")]
    public static void BuildExplore()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Farm" &&
            !EditorUtility.DisplayDialog("Explore", $"Aktivna scena je '{scene.name}', nie 'Farm'. Pokracovat aj tak?", "Ano", "Nie"))
            return;

        var old = scene.GetRootGameObjects().Where(g => g.name == "Explore").ToArray();
        if (old.Length > 0 &&
            !EditorUtility.DisplayDialog("Explore", "Lokacia sa vygeneruje nanovo a prepise prefab Explore. Rucne upravy v nom sa stratia (vratit sa daju cez git).", "Prestavat", "Zrusit"))
            return;

        if (!RunGenerator(ExploreGeneratorScript))
        {
            if (!File.Exists(ExploreJsonPath)) { EditorUtility.DisplayDialog("Explore", "Generator zlyhal a ulozena mapa neexistuje. Detaily su v Console.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Explore", "Generator (Python) sa nepodarilo spustit, detaily su v Console. Postavit poslednu ulozenu mapu?", "Postavit", "Zrusit"))
                return;
        }

        AssetDatabase.Refresh();
        ArtImportSettings.FixAll();
        var map = JsonUtility.FromJson<MapData>(File.ReadAllText(ExploreJsonPath));
        var tiles = UpdateTileAssets();
        var prefabs = UpdatePrefabs(); // aj nove zbieratelne veci (Mushroom, ScrapPile) s Gatherable

        Undo.SetCurrentGroupName("Vygenerovat Explore");
        foreach (var go in old) Undo.DestroyObjectImmediate(go);

        // Stavia sa v (0, 0) rovnakymi funkciami ako farma, nakoniec sa cely objekt posunie na ExploreOrigin.
        var root = new GameObject("Explore");
        Undo.RegisterCreatedObjectUndo(root, "Explore");
        BuildGround(root.transform, map, tiles);
        BuildEdges(root.transform, map.width, map.height);
        PlaceNature(root.transform, map, prefabs);

        var points = Child(root.transform, "Points");
        Marker(points, "ArrivalFromFarm", new Vector3(ExploreEntryX + 0.5f, 3.5f), 3);
        var exit = Marker(points, "FarmExit", new Vector3(ExploreEntryX + 0.5f, 0.75f), 5);
        var trigger = exit.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(3, 1.5f);

        root.transform.position = ExploreOrigin;
        SetupExploreLocation(root);
        SaveAsMapPrefab(root, ExplorePrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Explore {map.width}x{map.height} (seed {map.seed}) hotove na {ExploreOrigin}. Nezabudni ulozit scenu (Ctrl+S).");
    }

    /// Farma: Location (hranice kamery, hore aj vyhlad z kopca ako v SetupCamera) a vychod hore na ceste do Explore.
    static void SetupFarmLocation(GameObject farm)
    {
        SetupLocation(farm, FarmName, new Rect(0, 0, Width, Height - CliffRows + BackdropHeight));
        var points = farm.transform.Find("Points");
        if (points == null) { Debug.LogWarning("Environment/Points chyba, vychod na Explore nenastaveny. Prestavaj mapu."); return; }

        // Prichod na farmu: na ceste kusok pod vychodom (mimo jeho triggra, inak by hraca hned poslalo spat).
        if (points.Find("ArrivalFromExplore") == null)
            Marker(points, "ArrivalFromExplore", new Vector3(ExitX + 0.5f, Height - CliffRows - 2f), 3);
        var exit = points.Find("ExploreExit");
        if (exit != null) SetupExit(exit.gameObject, ExploreName, "Points/ArrivalFromFarm");
    }

    /// Explore: Location a vychod dole na farmu.
    static void SetupExploreLocation(GameObject explore)
    {
        SetupLocation(explore, ExploreName, new Rect(0, 0, ExploreWidth, ExploreHeight));
        var exit = explore.transform.Find("Points/FarmExit");
        if (exit != null) SetupExit(exit.gameObject, FarmName, "Points/ArrivalFromExplore");
    }

    static Location SetupLocation(GameObject root, string displayName, Rect area)
    {
        var location = root.GetComponent<Location>();
        if (location == null) location = Undo.AddComponent<Location>(root);
        var so = new SerializedObject(location);
        so.FindProperty("displayName").stringValue = displayName;
        so.FindProperty("area").rectValue = area;
        so.ApplyModifiedProperties();
        return location;
    }

    /// Vychod hlada ciel podla mena (farma a Explore su samostatne prefaby, priamy odkaz medzi nimi sa neda ulozit).
    static void SetupExit(GameObject exit, string targetName, string arrivalPath)
    {
        var col = exit.GetComponent<BoxCollider2D>();
        if (col == null) { col = exit.AddComponent<BoxCollider2D>(); col.size = new Vector2(3, 2); }
        col.isTrigger = true;

        var link = exit.GetComponent<LocationExit>();
        if (link == null) link = exit.AddComponent<LocationExit>();
        var so = new SerializedObject(link);
        so.FindProperty("targetName").stringValue = targetName;
        so.FindProperty("arrivalPath").stringValue = arrivalPath;
        so.FindProperty("target").objectReferenceValue = null;
        so.FindProperty("arrival").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
