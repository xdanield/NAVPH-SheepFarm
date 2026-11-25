using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Stavia mapu farmy v scene Farm.
///
/// Baa-ricade > Vygenerovat mapu
///     Spusti Python generator (Tools/mapgen/farm_map.py), ktory vymysli teren a rozmiestni stromy, kriky,
///     kvety... do Assets/Art/Map/farm_map.json a namaluje zem do Assets/Art/Map/farm_ground.png.
///     Potom zmaze objekty "Environment" a "Backdrop" v scene a postavi mapu podla toho suboru.
///     Ak Python nie je nainstalovany, postavi poslednu ulozenu mapu (subor je v gite).
/// Baa-ricade > Aktualizovat sprity z Aseprite
///     Nic neprestavuje, len do Tile assetov, prefabov a pozadia da nakreslene sprity z Assets/Art.
///     Kym frame v .aseprite nie je nakresleny, pouzije sa placeholder.
///
/// Konstanty nizsie (velkost mapy, ohrada, dom, crafting, zombie pruhy) cita aj Python generator,
/// takze ich staci zmenit tu. Nemen ich format (generator ich hlada podla mena).
///
/// Layout (pohlad zhora, 1 tile = 16 px = 1 unit, lavy dolny roh je (0, 0)):
///
///    ~~~~~~~~~~ vyhlad: obloha, zapad slnka, hory, mesto vlavo (pozadie, parallax) ~~~~~~~~~~
///    ########## okraj kopca (utes) ##########  ##  <- cestou hore sa ide na Explore
///    TT                                      ||     DOM   TT
///    TT les   bodce [=====  OHRADA  =====]   ||=====dvere TT
///    TT  <- zombie  [     ovce        ]             |
///    TT             [=================]       CRAFTING (stol, hrniec, truhla)
///    ≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈ potok ≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈≈
///    nedostupny pas (kamene, kriky)
/// </summary>
public static partial class PlaceholderMapBuilder
{
    public const int Width = 64;
    public const int Height = 32;
    const int BackdropHeight = 11;                                   // kolko units pozadia vidno nad mapou
    const int CliffRows = 2;                                         // horne riadky mapy = utes
    const int ForestDepth = 7;                                       // les vlavo (hranica je zvlnena)
    static readonly RectInt Pen = new RectInt(18, 9, 23, 18);        // ohrada vratane plotu
    static readonly RectInt CraftArea = new RectInt(46, 10, 13, 9);  // hlinena plocha pod domom
    static readonly Vector2Int HouseDoor = new Vector2Int(52, 22);   // spodok domu v strede
    const int HouseWidth = 8, HouseHeight = 7;
    const int ExitX = 44;                                            // stred cesty, ktora vedie hore na Explore
    static readonly int[] ZombieLanes = { 11, 17, 23 };              // riadky, kde zombie vychadzaju z lesa
    const int Seed = 12345;

    const string PlaceholderDir = "Assets/Art/Placeholder";
    const string TileAssetDir = "Assets/Art/TileAssets";
    const string PrefabDir = "Assets/Prefabs/Environment";
    const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
    const int PlaceholderScale = 1; // placeholdery su kreslene po 16 px na tile

    // Kamera: 320x180 px = 20x11 tiles. Na 1080p presne 6x, na 1440p 8x, na 4K 12x.
    const int CameraWidth = 320, CameraHeight = 180;

    // ---------------------------------------------------------------- definicie

    class PrefabDef
    {
        public string Name, Aseprite; public int Frame; public Func<Px> Paint;
        public Vector2 ColliderSize, ColliderOffset; public bool Trigger;
    }

    class BackdropDef
    {
        public string Name, Aseprite; public float Follow; public int Order; public Func<Px> Paint;
    }

    // Placeholdery pre dlazdice, kym nie su nakreslene (ostatne dlazdice dostanu bodku).
    static readonly Dictionary<string, Func<Px>> TilePlaceholders = new Dictionary<string, Func<Px>>
    {
        ["grass"] = () => Px.Noise(16, 16, "4f8a3c", "7bb54a", 0.12f, 1),
        ["grass_dark"] = () => Px.Noise(16, 16, "2f5d34", "1e3d23", 0.18f, 2),
        ["dirt"] = () => Px.Noise(16, 16, "a0784a", "8b5a2b", 0.15f, 3),
        ["water"] = Px.Water,
        ["cliff"] = Px.Cliff,
    };

    static readonly PrefabDef[] Prefabs =
    {
        new PrefabDef { Name = "Tree", Aseprite = "Objects/tree", Paint = Px.Tree, ColliderSize = new Vector2(0.6f, 0.4f), ColliderOffset = new Vector2(0, 0.2f) },
        new PrefabDef { Name = "Rock", Aseprite = "Objects/rock", Paint = Px.Rock, ColliderSize = new Vector2(0.8f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
        new PrefabDef { Name = "GrassTuft", Aseprite = "Objects/grass_tuft", Paint = Px.GrassTuft, ColliderSize = new Vector2(0.8f, 0.6f), ColliderOffset = new Vector2(0, 0.3f), Trigger = true },
        new PrefabDef { Name = "BerryBush", Aseprite = "Objects/berry_bush", Paint = Px.BerryBush, ColliderSize = new Vector2(0.8f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
        // pen_fence.aseprite: 0 H s kolikom, 1 V s kolikom, 2 H bez kolika, 3 V bez kolika, 4-7 rohy (BL, BR, TL, TR)
        new PrefabDef { Name = "PenFence_H", Aseprite = "Objects/pen_fence", Frame = 0, Paint = () => Px.Fence("5d3a24", "8b5a2b", 0), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_V", Aseprite = "Objects/pen_fence", Frame = 1, Paint = () => Px.Fence("5d3a24", "8b5a2b", 1), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_H_Rail", Aseprite = "Objects/pen_fence", Frame = 2, Paint = () => Px.Fence("5d3a24", "8b5a2b", 0), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_V_Rail", Aseprite = "Objects/pen_fence", Frame = 3, Paint = () => Px.Fence("5d3a24", "8b5a2b", 1), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_Corner_BL", Aseprite = "Objects/pen_fence", Frame = 4, Paint = () => Px.Fence("5d3a24", "8b5a2b", 2), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_Corner_BR", Aseprite = "Objects/pen_fence", Frame = 5, Paint = () => Px.Fence("5d3a24", "8b5a2b", 2), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_Corner_TL", Aseprite = "Objects/pen_fence", Frame = 6, Paint = () => Px.Fence("5d3a24", "8b5a2b", 2), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_Corner_TR", Aseprite = "Objects/pen_fence", Frame = 7, Paint = () => Px.Fence("5d3a24", "8b5a2b", 2), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "Fence", Aseprite = "Objects/fence", Paint = () => Px.Fence("8b5a2b", "c08552", 1), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "Spikes", Aseprite = "Objects/spikes", Paint = Px.Spikes, ColliderSize = new Vector2(0.9f, 0.9f), ColliderOffset = new Vector2(0, 0.5f), Trigger = true },
        new PrefabDef { Name = "Chest", Aseprite = "Objects/chest", Paint = Px.Chest, ColliderSize = new Vector2(0.9f, 0.6f), ColliderOffset = new Vector2(0, 0.3f) },
        new PrefabDef { Name = "House", Aseprite = "Objects/house", Paint = Px.House, ColliderSize = new Vector2(7f, 3.5f), ColliderOffset = new Vector2(0, 1.75f) },
        new PrefabDef { Name = "CraftingTable", Aseprite = "Objects/crafting_table", Paint = Px.CraftingTable, ColliderSize = new Vector2(1.8f, 0.6f), ColliderOffset = new Vector2(0, 0.3f) },
        new PrefabDef { Name = "CookingPot", Aseprite = "Objects/cooking_pot", Paint = Px.CookingPot, ColliderSize = new Vector2(0.8f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
        // Zbieratelne veci z Explore (les: huby, ruiny: srot). Kym nie je .aseprite, maju placeholder.
        new PrefabDef { Name = "Mushroom", Aseprite = "Objects/mushroom", Paint = Px.Mushroom, ColliderSize = new Vector2(0.6f, 0.4f), ColliderOffset = new Vector2(0, 0.2f), Trigger = true },
        new PrefabDef { Name = "ScrapPile", Aseprite = "Objects/scrap_pile", Paint = Px.ScrapPile, ColliderSize = new Vector2(0.9f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
    };

    // Follow: ako velmi sa vrstva hybe s kamerou (1 = nekonecne daleko). Order: co je vpredu.
    static readonly BackdropDef[] Backdrop =
    {
        new BackdropDef { Name = "Sky", Aseprite = "Background/sky", Follow = 0.9f, Order = -300, Paint = Px.Sky },
        new BackdropDef { Name = "Mountains", Aseprite = "Background/mountains", Follow = 0.7f, Order = -290, Paint = Px.Mountains },
        new BackdropDef { Name = "City", Aseprite = "Background/city", Follow = 0.5f, Order = -280, Paint = Px.City },
    };

    // ---------------------------------------------------------------- menu

    [MenuItem("Baa-ricade/Vygenerovat mapu")]
    public static void BuildMap()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Farm" &&
            !EditorUtility.DisplayDialog("Mapa", $"Aktivna scena je '{scene.name}', nie 'Farm'. Pokracovat aj tak?", "Ano", "Nie"))
            return;

        var old = scene.GetRootGameObjects().Where(g => g.name == "Environment" || g.name == "Backdrop").ToArray();
        if (old.Length > 0 &&
            !EditorUtility.DisplayDialog("Mapa", "Objekty 'Environment' a 'Backdrop' sa zmazu a mapa sa vygeneruje nanovo. Rucne upravy v nich sa stratia (Ctrl+Z to vrati).", "Prestavat", "Zrusit"))
            return;

        if (!RunGenerator())
        {
            if (!File.Exists(MapJsonPath)) { EditorUtility.DisplayDialog("Mapa", "Generator mapy zlyhal a ulozena mapa neexistuje. Detaily su v Console.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Mapa", "Generator mapy (Python) sa nepodarilo spustit, detaily su v Console. Postavit poslednu ulozenu mapu?", "Postavit", "Zrusit"))
                return;
        }

        AssetDatabase.Refresh();     // novy farm_map.json, farm_ground.png a zmenene .aseprite subory
        ArtImportSettings.FixAll(); // vsetka grafika v Assets/Art na aktualne PPU a Point filter
        var map = JsonUtility.FromJson<MapData>(File.ReadAllText(MapJsonPath));
        var tiles = UpdateTileAssets();
        var prefabs = UpdatePrefabs();
        var backdrop = Backdrop.ToDictionary(b => b.Name, b => ResolveSprite(b.Aseprite, 0, $"Background/{b.Name}", b.Paint));

        Undo.SetCurrentGroupName("Vygenerovat mapu");
        foreach (var go in old) Undo.DestroyObjectImmediate(go);

        var root = new GameObject("Environment");
        Undo.RegisterCreatedObjectUndo(root, "Environment");
        BuildGround(root.transform, map, tiles);
        BuildEdges(root.transform);
        PlaceNature(root.transform, map, prefabs);
        PlaceFarm(root.transform, prefabs);

        var back = new GameObject("Backdrop");
        Undo.RegisterCreatedObjectUndo(back, "Backdrop");
        BuildBackdrop(back.transform, backdrop);

        SetupCharacters();
        PlaceActors();
        SetupCamera();
        SetupSorting();
        LinkLocations(); // vychod hore na ceste -> Explore (ak uz je postavene), PlaceholderMapBuilder.Explore.cs

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Mapa {map.width}x{map.height} (seed {map.seed}) hotova. Nezabudni ulozit scenu (Ctrl+S).");
    }

    [MenuItem("Baa-ricade/Aktualizovat sprity z Aseprite")]
    public static void RefreshSprites()
    {
        AssetDatabase.Refresh();
        UpdateTileAssets();
        UpdatePrefabs();
        SetupCharacters();

        // Pozadie nie je prefab, sprity menime priamo v scene.
        var back = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "Backdrop");
        if (back != null)
        {
            foreach (var def in Backdrop)
            {
                var sr = back.transform.Find(def.Name)?.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                Undo.RecordObject(sr, "Backdrop sprite");
                sr.sprite = ResolveSprite(def.Aseprite, 0, $"Background/{def.Name}", def.Paint);
            }
            EditorSceneManager.MarkSceneDirty(back.scene);
        }
        Debug.Log("Sprity v tiles, prefaboch a pozadi aktualizovane.");
    }

    // ---------------------------------------------------------------- assety

    /// Kazdy .aseprite v Assets/Art/Tiles (aj v podpriecinkoch) je sada dlazdic, kazdy snimok jedna varianta:
    /// Tiles/Decor/flowers.aseprite -> TileAssets/Decor/flowers.asset, flowers_1.asset, flowers_2.asset...
    /// Kluc v slovniku je cesta bez pripony ("grass", "Decor/flowers"), rovnako ako v farm_map.json.
    static Dictionary<string, TileBase[]> UpdateTileAssets()
    {
        EnsureFolder(TileAssetDir);
        var result = new Dictionary<string, TileBase[]>();
        const string tilesDir = "Assets/Art/Tiles/";
        var files = AssetDatabase.FindAssets("", new[] { "Assets/Art/Tiles" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(f => f.EndsWith(".aseprite"))
            .Distinct();
        foreach (string file in files)
        {
            string name = file.Substring(tilesDir.Length, file.Length - tilesDir.Length - ".aseprite".Length);
            string aseprite = "Tiles/" + name;
            Func<Px> paint = TilePlaceholders.TryGetValue(name, out var ph) ? ph : () => Px.Dot("ff00ff");
            ResolveSprite(aseprite, 0, aseprite, paint); // naimportuje .aseprite, ak treba
            int frames = Mathf.Max(1, AssetDatabase.LoadAllAssetsAtPath(file).OfType<Sprite>().Count(sp => sp.name.StartsWith("Frame_")));
            string dir = Path.GetDirectoryName($"{TileAssetDir}/{name}").Replace('\\', '/');
            string baseName = Path.GetFileName(name);
            EnsureFolder(dir);
            var variants = new TileBase[frames];
            for (int i = 0; i < frames; i++)
            {
                string path = i == 0 ? $"{dir}/{baseName}.asset" : $"{dir}/{baseName}_{i}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, path);
                }
                tile.sprite = ResolveSprite(aseprite, i, aseprite, paint);
                tile.colliderType = Tile.ColliderType.Grid; // kolizia je len na tilemapach, ktore maju collider
                EditorUtility.SetDirty(tile);
                variants[i] = tile;
            }
            result[name] = variants;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    static Dictionary<string, GameObject> UpdatePrefabs()
    {
        EnsureFolder(PrefabDir);
        var result = new Dictionary<string, GameObject>();
        foreach (var def in Prefabs)
        {
            string path = $"{PrefabDir}/{def.Name}.prefab";
            var sprite = ResolveSprite(def.Aseprite, def.Frame, $"Objects/{def.Name}", def.Paint);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                // Existujuci prefab: zmenime len sprite, ostatne upravy ostanu.
                var contents = PrefabUtility.LoadPrefabContents(path);
                var sr = contents.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = sprite;
                ApplyGathering(contents, def);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }
            else
            {
                var go = new GameObject(def.Name);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.spriteSortPoint = SpriteSortPoint.Pivot;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = def.ColliderSize;
                col.offset = def.ColliderOffset;
                col.isTrigger = def.Trigger;
                ApplyGathering(go, def);
                PrefabUtility.SaveAsPrefabAsset(go, path);
                UnityEngine.Object.DestroyImmediate(go);
            }
            result[def.Name] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return result;
    }

    /// Nakresleny frame z .aseprite, inak vygenerovany placeholder PNG.
    static Sprite ResolveSprite(string aseprite, int frame, string placeholderName, Func<Px> paint)
    {
        string asePath = $"Assets/Art/{aseprite}.aseprite";
        var sprites = AssetDatabase.LoadAllAssetsAtPath(asePath).OfType<Sprite>().ToArray();
        if (sprites.Length == 0 && File.Exists(asePath))
        {
            // Subor existuje, ale Unity ho este nenaimportovala -> naimportuj hned.
            AssetDatabase.ImportAsset(asePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            sprites = AssetDatabase.LoadAllAssetsAtPath(asePath).OfType<Sprite>().ToArray();
        }
        var drawn = sprites.FirstOrDefault(s => s.name == $"Frame_{frame}");
        if (drawn == null && frame == 0 && sprites.Length == 1 && !sprites[0].name.StartsWith("Frame_"))
            drawn = sprites[0];
        if (drawn != null) return drawn;
        if (sprites.Length > 0)
            Debug.LogWarning($"{asePath}: snimok {frame} sa nenasiel (sprity: {string.Join(", ", sprites.Select(s => s.name))}), pouzivam placeholder.");

        // Podpriecinok Tiles/ dostane pivot v strede, ostatne dole v strede (ArtImportSettings).
        string pngPath = $"{PlaceholderDir}/{placeholderName}.png";
        EnsureFolder(Path.GetDirectoryName(pngPath).Replace('\\', '/'));
        if (!File.Exists(pngPath))
        {
            File.WriteAllBytes(pngPath, paint().ToPng(PlaceholderScale));
            AssetDatabase.ImportAsset(pngPath);
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
    }

    // ---------------------------------------------------------------- mapa zo suboru (farm_map.json)

    const string MapJsonPath = "Assets/Art/Map/farm_map.json";
    const string GeneratorScript = "Tools/mapgen/farm_map.py";

    [Serializable] class MapData
    {
        public int width, height, seed, groundOrder;
        public string ground;
        public string[] tiles;          // "nazov:snimok", napr. "Decor/flowers:3"
        public MapLayer[] layers;
        public MapObject[] objects;     // zbieratelne veci (prefaby)
        public MapProp[] props;         // dekoracie (stromy, kriky, kamene, tiene...)
    }
    [Serializable] class MapLayer { public string name; public int order; public bool collision, hidden; public int[] cells; }
    [Serializable] class MapObject { public string prefab; public int x, y; }
    [Serializable] class MapProp { public string sprite; public int frame, order; public float x, y, cw, ch; public bool collider, flip; }

    /// Spusti Python generator mapy. Vrati false, ak sa nepodarilo (detaily v Console).
    static bool RunGenerator(string generator = GeneratorScript)
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string script = Path.Combine(root, generator);
        foreach (string exe in new[] { "python", "py", "python3" })
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe, $"\"{script}\"")
                {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    var err = proc.StandardError.ReadToEndAsync();
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode == 9009) continue; // Windows: "python" je len odkaz na Microsoft Store
                    if (proc.ExitCode == 0) { Debug.Log($"Generator mapy:\n{output}"); return true; }
                    Debug.LogError($"Generator mapy zlyhal ({exe}, kod {proc.ExitCode}):\n{err.Result}\n{output}");
                    return false;
                }
            }
            catch (System.ComponentModel.Win32Exception) { } // tento prikaz neexistuje, skus dalsi
        }
        Debug.LogError("Python som nenasla (skusala som python, py, python3). Nainstaluj Python 3 s balikmi numpy a pillow: pip install numpy pillow");
        return false;
    }

    /// Zem (namalovany obrazok) a tilemapy: kolizia potoka, utes, ozdoby.
    static void BuildGround(Transform root, MapData map, Dictionary<string, TileBase[]> tiles)
    {
        var groundSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/{map.ground}.png");
        if (groundSprite == null) Debug.LogWarning($"Assets/Art/{map.ground}.png sa nenasiel, zem bude prazdna.");
        var ground = new GameObject("Ground");
        ground.transform.SetParent(root, false);
        ground.transform.position = new Vector3(map.width / 2f, 0, 0); // pivot obrazka je dole v strede
        var gsr = ground.AddComponent<SpriteRenderer>();
        gsr.sprite = groundSprite;
        gsr.sortingOrder = map.groundOrder;

        // kluc "nazov:snimok" -> Tile
        var lookup = new TileBase[map.tiles.Length];
        for (int i = 0; i < map.tiles.Length; i++)
        {
            int colon = map.tiles[i].LastIndexOf(':');
            string name = map.tiles[i].Substring(0, colon);
            int frame = int.Parse(map.tiles[i].Substring(colon + 1));
            if (tiles.TryGetValue(name, out var v)) lookup[i] = v[Mathf.Min(frame, v.Length - 1)];
            else Debug.LogWarning($"Dlazdica '{name}' z mapy neexistuje v Assets/Art/Tiles.");
        }

        var gridGo = new GameObject("Grid", typeof(Grid));
        gridGo.transform.SetParent(root, false);
        foreach (var layer in map.layers)
        {
            var tm = CreateTilemap(gridGo.transform, layer.name, layer.order);
            if (layer.hidden) tm.GetComponent<TilemapRenderer>().enabled = false;
            if (layer.collision)
            {
                var go = tm.gameObject;
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                go.AddComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
                go.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
            }
            var positions = new List<Vector3Int>();
            var cells = new List<TileBase>();
            for (int i = 0; i < layer.cells.Length; i++)
            {
                int id = layer.cells[i];
                if (id < 0 || lookup[id] == null) continue;
                positions.Add(new Vector3Int(i % map.width, i / map.width, 0));
                cells.Add(lookup[id]);
            }
            tm.SetTiles(positions.ToArray(), cells.ToArray());
        }
    }

    /// Stromy, kriky, kamene (dekoracie) a zbieratelne veci zo suboru mapy.
    static void PlaceNature(Transform root, MapData map, Dictionary<string, GameObject> prefabs)
    {
        var nature = Child(root, "Nature");
        foreach (var o in map.objects)
        {
            if (prefabs.TryGetValue(o.prefab, out var prefab)) Spawn(prefab, nature, o.x, o.y);
            else Debug.LogWarning($"Prefab '{o.prefab}' z mapy neexistuje.");
        }

        var props = Child(root, "Props");
        var cache = new Dictionary<string, Sprite[]>();
        foreach (var p in map.props)
        {
            if (!cache.TryGetValue(p.sprite, out var sprites))
            {
                var all = AssetDatabase.LoadAllAssetsAtPath($"Assets/Art/{p.sprite}.aseprite").OfType<Sprite>().ToArray();
                sprites = all.Length == 1 ? all
                    : all.Where(s => s.name.StartsWith("Frame_")).OrderBy(s => int.Parse(s.name.Substring(6))).ToArray();
                cache[p.sprite] = sprites;
                if (sprites.Length == 0) Debug.LogWarning($"Assets/Art/{p.sprite}.aseprite nema sprity.");
            }
            if (sprites.Length == 0) continue;
            var go = new GameObject(Path.GetFileName(p.sprite));
            go.transform.SetParent(props, false);
            go.transform.position = new Vector3(p.x, p.y, 0);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites[Mathf.Min(p.frame, sprites.Length - 1)];
            sr.sortingOrder = p.order;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            sr.flipX = p.flip;
            if (p.collider)
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(p.cw, p.ch);
                col.offset = new Vector2(0, p.ch / 2);
            }
        }
    }

    static Tilemap CreateTilemap(Transform grid, string name, int order)
    {
        var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        go.transform.SetParent(grid, false);
        go.GetComponent<TilemapRenderer>().sortingOrder = order;
        return go.GetComponent<Tilemap>();
    }

    /// Neviditelne steny okolo mapy, aby nikto nevysiel von.
    static void BuildEdges(Transform root, int w = Width, int h = Height)
    {
        var edges = Child(root, "MapEdges");
        void Wall(string name, Vector2 center, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(edges, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider2D>().size = size;
        }
        Wall("Left", new Vector2(-0.5f, h / 2f), new Vector2(1, h + 2));
        Wall("Right", new Vector2(w + 0.5f, h / 2f), new Vector2(1, h + 2));
        Wall("Bottom", new Vector2(w / 2f, -0.5f), new Vector2(w + 2, 1));
        Wall("Top", new Vector2(w / 2f, h + 0.5f), new Vector2(w + 2, 1));
    }

    // ---------------------------------------------------------------- objekty

    /// Ohrada, obrana, dom, crafting a body (spawn zombie, start hraca, vychod na Explore).
    static void PlaceFarm(Transform root, Dictionary<string, GameObject> p)
    {
        // Ohrada: plot po obvode Pen. Root "Pen" je miesto pre skript s HP ohrady (P1-06).
        var pen = Child(root, "Pen");
        for (int x = Pen.xMin; x < Pen.xMax; x++)
        for (int y = Pen.yMin; y < Pen.yMax; y++)
        {
            bool vEdge = x == Pen.xMin || x == Pen.xMax - 1;
            bool hEdge = y == Pen.yMin || y == Pen.yMax - 1;
            if (vEdge && hEdge) Spawn(p[$"PenFence_Corner_{(y == Pen.yMin ? "B" : "T")}{(x == Pen.xMin ? "L" : "R")}"], pen, x, y);
            else if (hEdge) Spawn(p[FencePost(x - Pen.xMin, Pen.width) ? "PenFence_H" : "PenFence_H_Rail"], pen, x, y);
            else if (vEdge) Spawn(p[FencePost(y - Pen.yMin, Pen.height) ? "PenFence_V" : "PenFence_V_Rail"], pen, x, y);
        }

        // Obrana na start (GDD: hrac zacina s par plotmi a bodcami) na lavej strane ohrady.
        var defenses = Child(root, "Defenses");
        for (int y = Pen.yMin + 1; y < Pen.yMax - 1; y += 3) Spawn(p["Spikes"], defenses, Pen.xMin - 2, y);
        int mid = Pen.yMin + Pen.height / 2;
        for (int y = mid - 2; y <= mid + 2; y++) Spawn(p["Fence"], defenses, Pen.xMin - 4, y);

        // Dom a zakladna.
        var farm = Child(root, "Farm");
        var house = (GameObject)PrefabUtility.InstantiatePrefab(p["House"], farm);
        house.transform.position = new Vector3(HouseDoor.x, HouseDoor.y, 0);
        int craftY = CraftArea.yMin + CraftArea.height / 2;
        Spawn(p["CraftingTable"], farm, CraftArea.xMin + 3, craftY);
        Spawn(p["CookingPot"], farm, CraftArea.xMin + 7, craftY);
        Spawn(p["Chest"], farm, CraftArea.xMin + 10, craftY);

        // Body: spawn zombie v lese, start hraca pri dverach, vychod na Explore hore na ceste.
        var points = Child(root, "Points");
        foreach (int lane in ZombieLanes)
            Marker(points, $"ZombieSpawn_{lane}", new Vector3(1.5f, lane + 0.5f), 2).tag = "ZombieLane"; // ZombieAI hlada drahy podla tagu
        Marker(points, "PlayerStart", new Vector3(HouseDoor.x, HouseDoor.y - 2.5f), 3);
        var exit = Marker(points, "ExploreExit", new Vector3(ExitX + 0.5f, Height - 1f), 5);
        var trigger = exit.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(3, 2);
    }

    static void BuildBackdrop(Transform root, Dictionary<string, Sprite> sprites)
    {
        // Spodok pozadia je schovany za utesom, aby nebola medzera.
        var basePos = new Vector3(Width / 2f, Height - CliffRows + 0.5f, 0);
        foreach (var def in Backdrop)
        {
            var go = new GameObject(def.Name);
            go.transform.SetParent(root, false);
            go.transform.position = basePos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites[def.Name];
            sr.sortingOrder = def.Order;
            var parallax = new SerializedObject(go.AddComponent<Parallax>());
            parallax.FindProperty("follow").floatValue = def.Follow;
            parallax.FindProperty("anchorX").floatValue = Width / 2f;
            parallax.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// Kolik plotu na kazdom druhom policku, symetricky od oboch rohov (rovnake pravidlo ma farm_map.py).
    static bool FencePost(int i, int len)
    {
        if (len % 2 == 0 && i == len / 2) return false; // dva stredy: kolik len na jednom z nich
        return Mathf.Min(i, len - 1 - i) % 2 == 0;
    }

    static void Spawn(GameObject prefab, Transform parent, int x, int y)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = new Vector3(x + 0.5f, y, 0); // pivot je dole v strede = spodok bunky
    }

    static GameObject Marker(Transform parent, string name, Vector3 pos, int icon)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var content = EditorGUIUtility.IconContent($"sv_label_{icon}");
        if (content?.image is Texture2D tex) EditorGUIUtility.SetIconForObject(go, tex);
        return go;
    }

    // ---------------------------------------------------------------- postavy

    // Prefab, .aseprite, frame (prvy frame idle animacie), collider pri nohach.
    static readonly (string prefab, string aseprite, int frame, Vector2 size, Vector2 offset)[] Characters =
    {
        ("Player", "player", 0, new Vector2(0.6f, 0.3f), new Vector2(0, 0.15f)),  // idle_down
        ("Zombie", "zombie", 4, new Vector2(0.6f, 0.3f), new Vector2(0, 0.15f)),  // idle_side
        ("Sheep", "sheep", 4, new Vector2(1.0f, 0.4f), new Vector2(0, 0.2f)),     // idle_side
    };

    /// Postavam da sprite (nakresleny frame z .aseprite, inak Characters/Temp/*.png),
    /// pivot pri nohach pre sorting a collider len okolo noh (ako v Stardew).
    static void SetupCharacters()
    {
        foreach (var (prefabName, aseprite, frame, size, offset) in Characters)
        {
            string path = $"Assets/Prefabs/{prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;

            string asePath = $"Assets/Art/Characters/{aseprite}.aseprite";
            var sprite = AssetDatabase.LoadAllAssetsAtPath(asePath).OfType<Sprite>().FirstOrDefault(s => s.name == $"Frame_{frame}")
                      ?? AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Characters/Temp/{aseprite}.png");
            if (sprite == null) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = sprite;
                sr.color = Color.white;
                sr.drawMode = SpriteDrawMode.Simple;
                sr.spriteSortPoint = SpriteSortPoint.Pivot;
            }

            // Collider len ked chyba. Existujuci (rucne doladeny v prefabe) nechame tak.
            if (root.GetComponent<Collider2D>() == null)
            {
                var box = root.AddComponent<BoxCollider2D>();
                box.size = size;
                box.offset = offset;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---------------------------------------------------------------- scena

    static void PlaceActors()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            Undo.RecordObject(player.transform, "Player start");
            player.transform.position = new Vector3(HouseDoor.x, HouseDoor.y - 2.5f, 0);
        }

        var center = new Vector3(Pen.center.x, Pen.center.y, 0);
        foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (go.parent != null) continue;
            if (go.name.StartsWith("Sheep"))
            {
                Undo.RecordObject(go, "Sheep");
                go.position = center + new Vector3(UnityEngine.Random.Range(-6f, 6f), UnityEngine.Random.Range(-4f, 4f));
            }
            else if (go.name.StartsWith("Zombie"))
            {
                Undo.RecordObject(go, "Zombie");
                go.position = new Vector3(ForestDepth + 1.5f, ZombieLanes[1] + 0.5f, 0);
            }
        }
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("Main Camera nenajdena, Pixel Perfect Camera nenastavena."); return; }

        var ppc = cam.GetComponent<PixelPerfectCamera>();
        if (ppc == null) ppc = Undo.AddComponent<PixelPerfectCamera>(cam.gameObject);
        Undo.RecordObject(ppc, "Pixel Perfect Camera");
        ppc.assetsPPU = ArtImportSettings.PPU;
        ppc.refResolutionX = CameraWidth;
        ppc.refResolutionY = CameraHeight;
        ppc.gridSnapping = PixelPerfectCamera.GridSnapping.None;
        ppc.cropFrame = PixelPerfectCamera.CropFrame.None;

        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null)
        {
            var so = new SerializedObject(follow);
            so.FindProperty("clampToBounds").boolValue = true;
            so.FindProperty("boundsMin").vector2Value = Vector2.zero;
            // Hore kamera ukaze aj vyhlad z kopca.
            so.FindProperty("boundsMax").vector2Value = new Vector2(Width, Height - CliffRows + BackdropHeight);
            var target = so.FindProperty("target");
            var player = GameObject.FindWithTag("Player");
            if (target.objectReferenceValue == null && player != null) target.objectReferenceValue = player.transform;
            so.ApplyModifiedProperties();
        }
    }

    static void SetupSorting()
    {
        // Co je nizsie na obrazovke, kresli sa vpredu (hrac moze ist za strom aj pred neho).
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Renderer2DPath);
        if (data == null) { Debug.LogWarning($"{Renderer2DPath} nenajdeny, sorting nenastaveny."); return; }
        var so = new SerializedObject(data);
        so.FindProperty("m_TransparencySortMode").intValue = (int)TransparencySortMode.CustomAxis;
        so.FindProperty("m_TransparencySortAxis").vector3Value = new Vector3(0, 1, 0);
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ---------------------------------------------------------------- placeholder kreslenie

    class Px
    {
        public readonly int w, h;
        readonly Color32[] c;
        Px(int w, int h) { this.w = w; this.h = h; c = new Color32[w * h]; }

        static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var col);
            return col;
        }

        void Set(int x, int y, Color32 col) { if (x >= 0 && y >= 0 && x < w && y < h) c[y * w + x] = col; }
        bool Filled(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && c[y * w + x].a > 0;

        void Rect(int x0, int y0, int x1, int y1, string hex)
        {
            var col = Hex(hex);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) Set(x, y, col);
        }

        void Ellipse(float cx, float cy, float rx, float ry, string hex)
        {
            var col = Hex(hex);
            int x0 = Mathf.Max(0, (int)(cx - rx - 1)), x1 = Mathf.Min(w - 1, (int)(cx + rx + 1));
            int y0 = Mathf.Max(0, (int)(cy - ry - 1)), y1 = Mathf.Min(h - 1, (int)(cy + ry + 1));
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1f) Set(x, y, col);
            }
        }

        /// Silueta: kazdy stlpec x vyplni od spodku po vysku height(x).
        void Silhouette(Func<int, float> height, string hex)
        {
            var col = Hex(hex);
            for (int x = 0; x < w; x++)
            {
                int top = Mathf.Clamp(Mathf.RoundToInt(height(x)), 0, h);
                for (int y = 0; y < top; y++) Set(x, y, col);
            }
        }

        void Outline(string hex = "1a1c2c")
        {
            var col = Hex(hex);
            var add = new List<Vector2Int>();
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
                if (!Filled(x, y) && (Filled(x + 1, y) || Filled(x - 1, y) || Filled(x, y + 1) || Filled(x, y - 1)))
                    add.Add(new Vector2Int(x, y));
            foreach (var p in add) Set(p.x, p.y, col);
        }

        public byte[] ToPng(int scale = 1)
        {
            var tex = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false);
            var big = new Color32[w * h * scale * scale];
            for (int y = 0; y < h * scale; y++)
                for (int x = 0; x < w * scale; x++)
                    big[y * w * scale + x] = c[(y / scale) * w + x / scale];
            tex.SetPixels32(big);
            tex.Apply();
            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return png;
        }

        public static Px Dot(string hex)
        {
            var px = new Px(16, 16);
            px.Rect(7, 7, 8, 8, hex);
            return px;
        }

        public static Px Noise(int w, int h, string baseHex, string dotHex, float density, int seed)
        {
            var px = new Px(w, h);
            px.Rect(0, 0, w - 1, h - 1, baseHex);
            var rng = new System.Random(seed);
            var dot = Hex(dotHex);
            for (int i = 0; i < w * h; i++) if (rng.NextDouble() < density) px.c[i] = dot;
            return px;
        }

        public static Px Water()
        {
            var px = Noise(16, 16, "4a90c2", "2d5f8a", 0.08f, 6);
            var light = Hex("9fd3f0");
            for (int x = 2; x < 7; x++) px.Set(x, 11, light);
            for (int x = 9; x < 14; x++) px.Set(x, 4, light);
            return px;
        }

        public static Px Cliff()
        {
            var px = Noise(16, 16, "8a7a66", "5d4a3a", 0.15f, 7);
            px.Rect(0, 13, 15, 15, "4f8a3c"); // trava na hrane
            px.Rect(0, 12, 15, 12, "3b2a2a");
            px.Rect(3, 4, 7, 4, "5d4a3a");
            px.Rect(10, 8, 13, 8, "5d4a3a");
            return px;
        }

        public static Px Tree()
        {
            var px = new Px(32, 48);
            px.Rect(13, 0, 18, 16, "5d3a24");
            px.Rect(14, 0, 15, 16, "8b5a2b");
            px.Ellipse(16, 29, 13, 14, "2f5d34");
            px.Ellipse(13, 32, 8, 8, "4f8a3c");
            px.Ellipse(11, 35, 3, 3, "7bb54a");
            px.Outline();
            return px;
        }

        public static Px Rock()
        {
            var px = new Px(16, 16);
            px.Ellipse(8, 4.5f, 6.5f, 4.5f, "7c7c88");
            px.Ellipse(7, 5.5f, 4, 2.5f, "b8b8c0");
            px.Outline();
            return px;
        }

        public static Px GrassTuft()
        {
            var px = new Px(16, 16);
            int[] xs = { 4, 6, 8, 10, 12 };
            int[] hs = { 5, 8, 10, 7, 5 };
            for (int i = 0; i < xs.Length; i++) px.Rect(xs[i], 1, xs[i], hs[i], i % 2 == 0 ? "7bb54a" : "b5d96b");
            px.Outline("1e3d23");
            return px;
        }

        /// Pnik po vyrubani stromu, na rovnakom platne ako strom (pata sedi).
        public static Px Stump()
        {
            var px = new Px(32, 48);
            px.Rect(12, 0, 19, 4, "5d3a24");
            px.Rect(13, 0, 14, 4, "8b5a2b");
            px.Rect(12, 5, 19, 5, "c08552");
            px.Rect(14, 5, 17, 5, "e8c39e");
            px.Rect(9, 0, 11, 1, "5d3a24");  // korene
            px.Rect(20, 0, 22, 1, "5d3a24");
            px.Outline();
            return px;
        }

        /// Ker bez bobul.
        public static Px BerryBushEmpty()
        {
            var px = new Px(16, 16);
            px.Ellipse(8, 6, 7, 5.5f, "2f5d34");
            px.Ellipse(7, 7, 4, 3, "4f8a3c");
            px.Outline();
            return px;
        }

        public static Px BerryBush()
        {
            var px = new Px(16, 16);
            px.Ellipse(8, 6, 7, 5.5f, "2f5d34");
            px.Ellipse(7, 7, 4, 3, "4f8a3c");
            foreach (var (x, y) in new[] { (4, 5), (9, 8), (11, 4), (6, 9), (12, 7) }) px.Rect(x, y, x, y, "c0392b");
            px.Outline();
            return px;
        }

        /// kind: 0 vodorovny, 1 zvisly, 2 roh (stlpik)
        public static Px Fence(string dark, string light, int kind)
        {
            var px = new Px(16, 16);
            if (kind == 0)
            {
                px.Rect(0, 4, 15, 5, light);
                px.Rect(0, 9, 15, 10, light);
                px.Rect(2, 1, 4, 12, dark);
                px.Rect(11, 1, 13, 12, dark);
            }
            else if (kind == 1)
            {
                px.Rect(6, 0, 9, 15, light);
                px.Rect(5, 1, 10, 3, dark);
                px.Rect(5, 9, 10, 11, dark);
            }
            else
            {
                px.Rect(5, 1, 10, 13, dark);
                px.Rect(6, 2, 7, 12, light);
            }
            px.Outline();
            return px;
        }

        public static Px Spikes()
        {
            var px = new Px(16, 16);
            px.Rect(1, 1, 14, 3, "5d3a24");
            foreach (int sx in new[] { 2, 7, 12 })
                for (int i = 0; i < 6; i++) px.Rect(sx - (5 - i) / 3, 4 + i, sx + (5 - i) / 3, 4 + i, i > 3 ? "e4e4e4" : "b8b8c0");
            px.Outline();
            return px;
        }

        public static Px Chest()
        {
            var px = new Px(16, 16);
            px.Rect(1, 1, 14, 11, "8b5a2b");
            px.Rect(1, 8, 14, 9, "5d3a24");
            px.Rect(7, 6, 8, 9, "d9b25c");
            px.Outline();
            return px;
        }

        public static Px House()
        {
            var px = new Px(128, 112);
            px.Rect(8, 0, 119, 60, "c08552");                       // steny
            for (int y = 6; y < 60; y += 6) px.Rect(8, y, 119, y, "8b5a2b");
            for (int y = 56; y < 111; y++)                           // strecha
            {
                int inset = (y - 56) * 2 / 3;
                px.Rect(2 + inset, y, 125 - inset, y, (y / 5) % 2 == 0 ? "7a1f1f" : "c0392b");
            }
            px.Rect(54, 0, 73, 30, "5d3a24");                        // dvere
            px.Rect(69, 14, 70, 15, "d9b25c");
            foreach (int wx in new[] { 22, 86 })                     // okna
            {
                px.Rect(wx - 2, 22, wx + 21, 43, "5d3a24");
                px.Rect(wx, 24, wx + 19, 41, "9fd3f0");
                px.Rect(wx + 9, 24, wx + 10, 41, "5d3a24");
                px.Rect(wx, 32, wx + 19, 33, "5d3a24");
            }
            px.Outline();
            return px;
        }

        public static Px CraftingTable()
        {
            var px = new Px(32, 32);
            px.Rect(3, 0, 5, 12, "5d3a24");
            px.Rect(26, 0, 28, 12, "5d3a24");
            px.Rect(1, 12, 30, 18, "8b5a2b");
            px.Rect(1, 18, 30, 19, "c08552");
            px.Rect(6, 20, 13, 21, "b8b8c0");  // pila
            px.Rect(18, 20, 19, 25, "5d3a24"); // kladivo
            px.Rect(16, 25, 21, 27, "7c7c88");
            px.Outline();
            return px;
        }

        public static Px CookingPot()
        {
            var px = new Px(16, 24);
            px.Rect(3, 0, 12, 2, "5d3a24");     // drevo
            px.Rect(5, 2, 10, 5, "e07b1f");     // ohen
            px.Rect(7, 4, 8, 7, "f2a65a");
            px.Ellipse(8, 11, 6.5f, 5, "4a4a55");
            px.Rect(2, 14, 13, 15, "7c7c88");
            px.Outline();
            return px;
        }

        public static Px Mushroom()
        {
            var px = new Px(16, 16);
            px.Rect(4, 1, 5, 4, "e8dcc8");          // maly hribik vlavo
            px.Ellipse(4.5f, 5.5f, 3, 2, "c0392b");
            px.Rect(9, 1, 11, 6, "e8dcc8");         // velky v strede
            px.Ellipse(10, 8, 5, 3, "c0392b");
            px.Rect(8, 9, 8, 9, "ffffff");          // bodky
            px.Rect(11, 8, 11, 8, "ffffff");
            px.Rect(3, 6, 3, 6, "ffffff");
            px.Outline();
            return px;
        }

        public static Px ScrapPile()
        {
            var px = new Px(16, 16);
            px.Ellipse(8, 3.5f, 7, 3.5f, "5d4a3a");  // hlina pod tym
            px.Rect(2, 3, 9, 6, "7c7c88");          // plech
            px.Rect(3, 6, 8, 6, "b8b8c0");
            px.Rect(9, 2, 13, 8, "8a5a3a");         // hrdzava tyc / kus stroja
            px.Rect(10, 7, 12, 8, "c07a4a");
            px.Rect(5, 7, 6, 10, "4a4a55");         // trubka
            px.Rect(5, 10, 7, 10, "7c7c88");
            px.Outline();
            return px;
        }

        // ---- pozadie (1024 px x2 = sirka mapy)

        public static Px Sky()
        {
            var px = new Px(1024, 176);
            string[] bands = { "ffd38a", "f7b267", "f28c4a", "e0704a", "c25a5a", "a3546f", "7a4a73", "564271", "3d3d6b", "2c3e66" };
            for (int y = 0; y < 176; y++)
            {
                int band = Mathf.Min(bands.Length - 1, y * bands.Length / 176);
                px.Rect(0, y, px.w - 1, y, bands[band]);
            }
            px.Ellipse(560, 34, 30, 30, "ffcf6e");                   // slnko
            px.Ellipse(560, 34, 24, 24, "ffe2a8");
            return px;
        }

        public static Px Mountains()
        {
            var px = new Px(1024, 112);
            float Peak(int x, float cx, float hgt, float width) => Mathf.Max(0, hgt - Mathf.Abs(x - cx) * hgt / width);
            float Height(int x) => 28 + 3 * Mathf.Sin(x * 0.05f)
                + Mathf.Max(Peak(x, 170, 100, 140), Mathf.Max(Peak(x, 330, 68, 110), Mathf.Max(Peak(x, 720, 52, 120), Peak(x, 880, 70, 110))));
            px.Silhouette(Height, "8a6a8a");
            px.Silhouette(x => Height(x) > 88 ? Height(x) : 0, "e4d4e4");     // snehova cepica
            px.Silhouette(x => Mathf.Min(Height(x), 88), "8a6a8a");
            px.Silhouette(x => Height(x) * 0.55f, "6b4f73");                   // tien pri spodku
            return px;
        }

        public static Px City()
        {
            var px = new Px(1024, 80);
            var rng = new System.Random(8);
            for (int x = 120; x < 380;)                              // mesto vlavo
            {
                int bw = rng.Next(10, 26), bh = rng.Next(22, 64);
                px.Rect(x, 0, x + bw - 1, bh, "2b2438");
                for (int wx = x + 2; wx < x + bw - 2; wx += 4)
                    for (int wy = 6; wy < bh - 3; wy += 6)
                        if (rng.NextDouble() < 0.3) px.Rect(wx, wy, wx + 1, wy + 2, "f2a65a");
                x += bw + rng.Next(0, 4);
            }
            px.Silhouette(x => 20 + 6 * Mathf.Sin(x * 0.013f) + 4 * Mathf.Sin(x * 0.031f + 1), "3d5a3a"); // kopce
            px.Silhouette(x => 10 + 3 * Mathf.Sin(x * 0.02f + 2), "2f4a30");
            return px;
        }
    }
}
