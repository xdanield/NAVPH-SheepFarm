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
/// Generuje placeholder mapu farmy pre scenu Farm.
///
/// Baa-ricade > Vygenerovat placeholder mapu
///     Zmaze objekty "Environment" a "Backdrop" v scene a postavi mapu nanovo.
/// Baa-ricade > Aktualizovat sprity z Aseprite
///     Nic neprestavuje, len do Tile assetov, prefabov a pozadia da nakreslene sprity z Assets/Art.
///     Kym frame v .aseprite nie je nakresleny, pouzije sa placeholder.
///
/// Layout (pohlad zhora ako Stardew Valley, 1 tile = 16 px = 1 unit, lavy dolny roh je (0, 0)):
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
public static class PlaceholderMapBuilder
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
    const int PlaceholderScale = 2; // placeholdery su kreslene po 16 px na tile, zvacsene na 32 px

    // Kamera: 640x360 px = 20x11 tiles. Na 1080p presne 3x, na 1440p 4x, na 4K 6x.
    const int CameraWidth = 640, CameraHeight = 360;

    // ---------------------------------------------------------------- definicie

    class TileDef
    {
        public string Name, Aseprite; public bool Solid; public Func<Px> Paint;
    }

    class PrefabDef
    {
        public string Name, Aseprite; public int Frame; public Func<Px> Paint;
        public Vector2 ColliderSize, ColliderOffset; public bool Trigger;
    }

    class BackdropDef
    {
        public string Name, Aseprite; public float Follow; public int Order; public Func<Px> Paint;
    }

    static readonly TileDef[] Tiles =
    {
        new TileDef { Name = "grass", Aseprite = "Tiles/grass", Paint = () => Px.Noise(16, 16, "4f8a3c", "7bb54a", 0.12f, 1) },
        new TileDef { Name = "forest_floor", Aseprite = "Tiles/forest_floor", Paint = () => Px.Noise(16, 16, "2f5d34", "1e3d23", 0.18f, 2) },
        new TileDef { Name = "dirt", Aseprite = "Tiles/dirt", Paint = () => Px.Noise(16, 16, "a0784a", "8b5a2b", 0.15f, 3) },
        new TileDef { Name = "path", Aseprite = "Tiles/path", Paint = () => Px.Noise(16, 16, "d9b25c", "c08552", 0.12f, 4) },
        new TileDef { Name = "water", Aseprite = "Tiles/water", Solid = true, Paint = Px.Water },
        new TileDef { Name = "cliff", Aseprite = "Tiles/cliff", Solid = true, Paint = Px.Cliff },
    };

    static readonly PrefabDef[] Prefabs =
    {
        new PrefabDef { Name = "Tree", Aseprite = "Objects/tree", Paint = Px.Tree, ColliderSize = new Vector2(0.6f, 0.4f), ColliderOffset = new Vector2(0, 0.2f) },
        new PrefabDef { Name = "Rock", Aseprite = "Objects/rock", Paint = Px.Rock, ColliderSize = new Vector2(0.8f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
        new PrefabDef { Name = "GrassTuft", Aseprite = "Objects/grass_tuft", Paint = Px.GrassTuft, ColliderSize = new Vector2(0.8f, 0.6f), ColliderOffset = new Vector2(0, 0.3f), Trigger = true },
        new PrefabDef { Name = "BerryBush", Aseprite = "Objects/berry_bush", Paint = Px.BerryBush, ColliderSize = new Vector2(0.8f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
        new PrefabDef { Name = "PenFence_H", Aseprite = "Objects/pen_fence", Frame = 0, Paint = () => Px.Fence("5d3a24", "8b5a2b", 0), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_V", Aseprite = "Objects/pen_fence", Frame = 1, Paint = () => Px.Fence("5d3a24", "8b5a2b", 1), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "PenFence_Corner", Aseprite = "Objects/pen_fence", Frame = 2, Paint = () => Px.Fence("5d3a24", "8b5a2b", 2), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "Fence", Aseprite = "Objects/fence", Paint = () => Px.Fence("8b5a2b", "c08552", 1), ColliderSize = Vector2.one, ColliderOffset = new Vector2(0, 0.5f) },
        new PrefabDef { Name = "Spikes", Aseprite = "Objects/spikes", Paint = Px.Spikes, ColliderSize = new Vector2(0.9f, 0.9f), ColliderOffset = new Vector2(0, 0.5f), Trigger = true },
        new PrefabDef { Name = "Chest", Aseprite = "Objects/chest", Paint = Px.Chest, ColliderSize = new Vector2(0.9f, 0.6f), ColliderOffset = new Vector2(0, 0.3f) },
        new PrefabDef { Name = "House", Aseprite = "Objects/house", Paint = Px.House, ColliderSize = new Vector2(7f, 3.5f), ColliderOffset = new Vector2(0, 1.75f) },
        new PrefabDef { Name = "CraftingTable", Aseprite = "Objects/crafting_table", Paint = Px.CraftingTable, ColliderSize = new Vector2(1.8f, 0.6f), ColliderOffset = new Vector2(0, 0.3f) },
        new PrefabDef { Name = "CookingPot", Aseprite = "Objects/cooking_pot", Paint = Px.CookingPot, ColliderSize = new Vector2(0.8f, 0.5f), ColliderOffset = new Vector2(0, 0.25f) },
    };

    // Follow: ako velmi sa vrstva hybe s kamerou (1 = nekonecne daleko). Order: co je vpredu.
    static readonly BackdropDef[] Backdrop =
    {
        new BackdropDef { Name = "Sky", Aseprite = "Background/sky", Follow = 0.9f, Order = -300, Paint = Px.Sky },
        new BackdropDef { Name = "Mountains", Aseprite = "Background/mountains", Follow = 0.7f, Order = -290, Paint = Px.Mountains },
        new BackdropDef { Name = "City", Aseprite = "Background/city", Follow = 0.5f, Order = -280, Paint = Px.City },
    };

    // ---------------------------------------------------------------- menu

    [MenuItem("Baa-ricade/Vygenerovat placeholder mapu")]
    public static void BuildMap()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Farm" &&
            !EditorUtility.DisplayDialog("Placeholder mapa", $"Aktivna scena je '{scene.name}', nie 'Farm'. Pokracovat aj tak?", "Ano", "Nie"))
            return;

        var old = scene.GetRootGameObjects().Where(g => g.name == "Environment" || g.name == "Backdrop").ToArray();
        if (old.Length > 0 &&
            !EditorUtility.DisplayDialog("Placeholder mapa", "Objekty 'Environment' a 'Backdrop' sa zmazu a mapa sa vygeneruje nanovo. Rucne upravy v nich sa stratia (Ctrl+Z to vrati).", "Prestavat", "Zrusit"))
            return;

        ArtImportSettings.FixAll(); // vsetka grafika v Assets/Art na aktualne PPU a Point filter
        var tiles = UpdateTileAssets();
        var prefabs = UpdatePrefabs();
        var backdrop = Backdrop.ToDictionary(b => b.Name, b => ResolveSprite(b.Aseprite, 0, $"Background/{b.Name}", b.Paint));

        Undo.SetCurrentGroupName("Vygenerovat placeholder mapu");
        foreach (var go in old) Undo.DestroyObjectImmediate(go);

        var root = new GameObject("Environment");
        Undo.RegisterCreatedObjectUndo(root, "Environment");
        BuildTilemaps(root.transform, tiles);
        BuildEdges(root.transform);
        PlaceObjects(root.transform, prefabs);

        var back = new GameObject("Backdrop");
        Undo.RegisterCreatedObjectUndo(back, "Backdrop");
        BuildBackdrop(back.transform, backdrop);

        SetupCharacters();
        PlaceActors();
        SetupCamera();
        SetupSorting();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Placeholder mapa {Width}x{Height} hotova. Nezabudni ulozit scenu (Ctrl+S).");
    }

    [MenuItem("Baa-ricade/Aktualizovat sprity z Aseprite")]
    public static void RefreshSprites()
    {
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

    static Dictionary<string, TileBase> UpdateTileAssets()
    {
        EnsureFolder(TileAssetDir);
        var result = new Dictionary<string, TileBase>();
        foreach (var def in Tiles)
        {
            string path = $"{TileAssetDir}/{def.Name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = ResolveSprite(def.Aseprite, 0, $"Tiles/{def.Name}", def.Paint);
            tile.colliderType = def.Solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            result[def.Name] = tile;
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
        var drawn = sprites.FirstOrDefault(s => s.name == $"Frame_{frame}");
        if (drawn == null && frame == 0 && sprites.Length == 1 && !sprites[0].name.StartsWith("Frame_"))
            drawn = sprites[0];
        if (drawn != null) return drawn;

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

    // ---------------------------------------------------------------- tvar mapy

    static int ForestEdge(int y) => ForestDepth + Mathf.RoundToInt(1.5f * Mathf.Sin(y * 0.4f) + Mathf.Sin(y * 0.17f + 2f));
    static int RiverY(int x) => 4 + Mathf.RoundToInt(1.2f * Mathf.Sin(x * 0.12f + 1f));
    static bool InRiver(int x, int y, int margin = 0) => Mathf.Abs(y - RiverY(x)) <= 1 + margin;
    static bool BelowRiver(int x, int y) => y < RiverY(x) - 1;
    static bool InForest(int x, int y) => x < ForestEdge(y);
    static bool InCliff(int x, int y) => y >= Height - CliffRows && Mathf.Abs(x - ExitX) > 1;
    static bool InCraft(int x, int y) => CraftArea.Contains(new Vector2Int(x, y));
    static bool InPen(int x, int y, int margin = 0) =>
        x >= Pen.xMin - margin && x < Pen.xMax + margin && y >= Pen.yMin - margin && y < Pen.yMax + margin;
    static bool InHouse(int x, int y, int margin = 0) =>
        Mathf.Abs(x + 0.5f - HouseDoor.x) <= HouseWidth / 2f + margin && y >= HouseDoor.y - margin && y < HouseDoor.y + HouseHeight + margin;

    // Cesta: od dveri dole ku craftingu, od dveri dolava a potom hore k vychodu na Explore.
    static readonly RectInt[] PathRects =
    {
        new RectInt(HouseDoor.x - 1, CraftArea.yMax - 1, 2, HouseDoor.y - CraftArea.yMax + 1),
        new RectInt(ExitX - 1, HouseDoor.y - 2, HouseDoor.x - ExitX, 2),
        new RectInt(ExitX - 1, HouseDoor.y - 2, 3, Height - HouseDoor.y + 2),
    };

    static bool OnPath(int x, int y, int margin = 0)
    {
        foreach (var r in PathRects)
            if (x >= r.xMin - margin && x < r.xMax + margin && y >= r.yMin - margin && y < r.yMax + margin) return true;
        return false;
    }

    static void BuildTilemaps(Transform root, Dictionary<string, TileBase> t)
    {
        var gridGo = new GameObject("Grid", typeof(Grid));
        gridGo.transform.SetParent(root, false);

        var ground = CreateTilemap(gridGo.transform, "Ground", -100);
        CreateTilemap(gridGo.transform, "Decor", -90); // prazdna, na kvety, kamienky a pod.
        var collision = CreateTilemap(gridGo.transform, "Collision", -80);

        var colGo = collision.gameObject;
        colGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var composite = colGo.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        colGo.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var cell = new Vector3Int(x, y, 0);
            string g = OnPath(x, y) ? "path"
                     : InCraft(x, y) ? "dirt"
                     : InForest(x, y) && !BelowRiver(x, y) ? "forest_floor"
                     : "grass";
            ground.SetTile(cell, t[g]);

            if (InCliff(x, y)) collision.SetTile(cell, t["cliff"]);
            else if (InRiver(x, y)) collision.SetTile(cell, t["water"]);
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
    static void BuildEdges(Transform root)
    {
        var edges = Child(root, "MapEdges");
        void Wall(string name, Vector2 center, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(edges, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider2D>().size = size;
        }
        Wall("Left", new Vector2(-0.5f, Height / 2f), new Vector2(1, Height + 2));
        Wall("Right", new Vector2(Width + 0.5f, Height / 2f), new Vector2(1, Height + 2));
        Wall("Bottom", new Vector2(Width / 2f, -0.5f), new Vector2(Width + 2, 1));
        Wall("Top", new Vector2(Width / 2f, Height + 0.5f), new Vector2(Width + 2, 1));
    }

    // ---------------------------------------------------------------- objekty

    static void PlaceObjects(Transform root, Dictionary<string, GameObject> p)
    {
        var rng = new System.Random(Seed);
        var blocked = new HashSet<Vector2Int>();

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            bool lane = InForest(x, y) && ZombieLanes.Any(l => Mathf.Abs(y - l) <= 1);
            if (InPen(x, y, 2) || InHouse(x, y, 1) || CraftArea.Contains(new Vector2Int(x, y)) || OnPath(x, y, 1) ||
                InRiver(x, y, 1) || InCliff(x, y) || y >= Height - CliffRows || lane ||
                (x >= Pen.xMin - 5 && x < Pen.xMin && InPen(Pen.xMin, y))) // miesto pre obranu
                blocked.Add(new Vector2Int(x, y));
        }

        var nature = Child(root, "Nature");

        bool TryPlace(string prefab, int x, int y)
        {
            var c = new Vector2Int(x, y);
            if (x < 0 || y < 0 || x >= Width || y >= Height || blocked.Contains(c)) return false;
            Spawn(p[prefab], nature, x, y);
            blocked.Add(c);
            blocked.Add(c + Vector2Int.right);
            blocked.Add(c + Vector2Int.up);
            blocked.Add(c + Vector2Int.left);
            return true;
        }

        // Stromy po blokoch 2x2 s nahodnym posunom: husto v lese vlavo, v pravom hornom rohu
        // a za potokom, sem-tam pod utesom.
        for (int bx = 0; bx < Width; bx += 2)
        for (int by = 0; by < Height; by += 2)
        {
            int x = bx + rng.Next(2), y = by + rng.Next(2);
            float chance = BelowRiver(x, y) ? 0.25f
                         : InForest(x, y) ? 0.75f
                         : x >= Width - 6 && y >= HouseDoor.y - 2 ? 0.5f
                         : x >= Width - 3 ? 0.35f
                         : y >= Height - CliffRows - 3 ? 0.15f
                         : 0.02f;
            if (rng.NextDouble() < chance) TryPlace("Tree", x, y);
        }

        // Drobnosti: trava na luke, kamene a kriky za potokom.
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            double r = rng.NextDouble();
            if (BelowRiver(x, y))
            {
                if (r < 0.08) TryPlace("Rock", x, y);
                else if (r < 0.14) TryPlace("BerryBush", x, y);
            }
            else if (!InForest(x, y) && r < 0.025) TryPlace("GrassTuft", x, y);
        }

        // Ohrada: plot po obvode Pen. Root "Pen" je miesto pre skript s HP ohrady (P1-06).
        var pen = Child(root, "Pen");
        for (int x = Pen.xMin; x < Pen.xMax; x++)
        for (int y = Pen.yMin; y < Pen.yMax; y++)
        {
            bool vEdge = x == Pen.xMin || x == Pen.xMax - 1;
            bool hEdge = y == Pen.yMin || y == Pen.yMax - 1;
            if (vEdge && hEdge) Spawn(p["PenFence_Corner"], pen, x, y);
            else if (hEdge) Spawn(p["PenFence_H"], pen, x, y);
            else if (vEdge) Spawn(p["PenFence_V"], pen, x, y);
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
            Marker(points, $"ZombieSpawn_{lane}", new Vector3(1.5f, lane + 0.5f), 2);
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

            foreach (var old in root.GetComponents<Collider2D>().Where(c => !(c is BoxCollider2D)))
                UnityEngine.Object.DestroyImmediate(old);
            var box = root.GetComponent<BoxCollider2D>();
            if (box == null) box = root.AddComponent<BoxCollider2D>();
            box.size = size;
            box.offset = offset;

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
