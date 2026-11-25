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
///     a explore_ground.png), zmaze objekt "Explore" v scene a postavi ho nanovo. Farmu nemeni, len prepoji
///     vychod hore na ceste (ExploreExit) s prichodom do Explore a naspat.
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
            !EditorUtility.DisplayDialog("Explore", "Objekt 'Explore' sa zmaze a lokacia sa vygeneruje nanovo. Rucne upravy v nom sa stratia (Ctrl+Z to vrati).", "Prestavat", "Zrusit"))
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

        LinkLocations();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Explore {map.width}x{map.height} (seed {map.seed}) hotove na {ExploreOrigin}. Nezabudni ulozit scenu (Ctrl+S).");
    }

    /// Farma a Explore dostanu Location (hranice kamery) a vychody sa prepoja navzajom.
    /// Vola sa po stavbe farmy aj Explore, takze prestavanie jednej lokacie spojenie neporusi.
    static void LinkLocations()
    {
        var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
        var farm = roots.FirstOrDefault(g => g.name == "Environment");
        var explore = roots.FirstOrDefault(g => g.name == "Explore");

        // Farma: kamera ukaze hore aj vyhlad z kopca (pozadie), rovnako ako v SetupCamera.
        var farmLocation = farm != null ? SetupLocation(farm, FarmName, new Rect(0, 0, Width, Height - CliffRows + BackdropHeight)) : null;
        var exploreLocation = explore != null ? SetupLocation(explore, ExploreName, new Rect(0, 0, ExploreWidth, ExploreHeight)) : null;
        if (farmLocation == null || exploreLocation == null) return;

        var farmPoints = farm.transform.Find("Points");
        var farmExit = farmPoints != null ? farmPoints.Find("ExploreExit") : null;
        var exploreExit = explore.transform.Find("Points/FarmExit");
        var exploreArrival = explore.transform.Find("Points/ArrivalFromFarm");
        if (farmExit == null || exploreExit == null || exploreArrival == null)
        {
            Debug.LogWarning("Lokacie sa nepodarilo prepojit: chyba Environment/Points/ExploreExit alebo Explore/Points/FarmExit. Prestavaj mapu.");
            return;
        }

        // Prichod na farmu: na ceste kusok pod vychodom (mimo jeho triggra, inak by hraca hned poslalo spat).
        var farmArrival = farmPoints.Find("ArrivalFromExplore");
        if (farmArrival == null)
        {
            var go = Marker(farmPoints, "ArrivalFromExplore", new Vector3(ExitX + 0.5f, Height - CliffRows - 2f), 3);
            Undo.RegisterCreatedObjectUndo(go, "Arrival");
            farmArrival = go.transform;
        }

        WireExit(farmExit.gameObject, exploreLocation, exploreArrival);
        WireExit(exploreExit.gameObject, farmLocation, farmArrival);
        EditorSceneManager.MarkSceneDirty(farm.scene);
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

    static void WireExit(GameObject exit, Location target, Transform arrival)
    {
        var col = exit.GetComponent<BoxCollider2D>();
        if (col == null) { col = Undo.AddComponent<BoxCollider2D>(exit); col.size = new Vector2(3, 2); }
        col.isTrigger = true;

        var link = exit.GetComponent<LocationExit>();
        if (link == null) link = Undo.AddComponent<LocationExit>(exit);
        var so = new SerializedObject(link);
        so.FindProperty("target").objectReferenceValue = target;
        so.FindProperty("arrival").objectReferenceValue = arrival;
        so.ApplyModifiedProperties();
    }
}
