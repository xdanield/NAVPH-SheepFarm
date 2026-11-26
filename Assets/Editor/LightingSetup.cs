using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Baa-ricade > Nastavit osvetlenie
///     Prida do sceny Farm den/noc a teple svetla:
///     - objekt "Lighting" s Global Light 2D a DayNightLighting (farba a sila podla casu, nahlad slidrom),
///     - svetla v oknach domu, lampas pri dverach, ohen pod hrncom (hrac vlastne svetlo nema),
///     - SpriteShadow (tien v tvare spritu od slnka aj lamp) na prefaby domu, plotov, stola, truhlice...
///       aj na stromy/kriky/kamene z generatora mapy (namalovane tiene Props/shadows zmaze).
///       Unity Shadow Caster 2D sa nepouziva, stare zo sceny zmaze.
///     Da sa spustit znova, stare svetla zmaze a postavi nanovo. Vysledok ulozi ako prefab
///     Assets/Prefabs/Map/Lighting.prefab (v scene je len instancia).
///
/// LightingSetup.CapturePreview (len z prikazoveho riadku, batchmode)
///     Nastavi osvetlenie v pamati (scenu neulozi) a ulozi screenshoty pre rozne casy dna do -outDir.
/// </summary>
public static class LightingSetup
{
    static readonly Color WindowColor = new Color(1f, 0.72f, 0.36f);
    static readonly Color LanternColor = new Color(1f, 0.66f, 0.32f);
    static readonly Color FireColor = new Color(1f, 0.5f, 0.2f);

    [MenuItem("Baa-ricade/Nastavit osvetlenie")]
    public static void SetupMenu()
    {
        Undo.SetCurrentGroupName("Nastavit osvetlenie");
        int added = AddSpriteShadows();
        if (added > 0) Debug.Log($"Osvetlenie: SpriteShadow pridany na {added} prefabov.");
        Setup();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Osvetlenie hotove. Cas posuvas na objekte Lighting (Nahlad), v hre drz T na zrychlenie casu. Nezabudni ulozit scenu (Ctrl+S).");
    }

    /// <param name="savePrefab">false = len v pamati (screenshoty v batchmode), Lighting.prefab sa neprepise.</param>
    public static DayNightLighting Setup(bool savePrefab = true)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects().Where(g => g.name == "Lighting")) Undo.DestroyObjectImmediate(go);

        // ---- globalne svetlo + den/noc
        var root = new GameObject("Lighting");
        Undo.RegisterCreatedObjectUndo(root, "Lighting");
        var global = new GameObject("Global Light 2D").AddComponent<Light2D>();
        global.transform.SetParent(root.transform, false);
        global.lightType = Light2D.LightType.Global;
        AllLayers(global);

        var dn = root.AddComponent<DayNightLighting>();
        var so = new SerializedObject(dn);
        // cycle nenastavujeme: Lighting je prefab (nemoze odkazovat na scenu), DayNightLighting si ho najde v Awake.
        so.FindProperty("globalLight").objectReferenceValue = global;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---- teple svetla na farme
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Environment");
        var house = Find(env, "House");
        if (house != null)
        {
            var h = house.transform.position;
            // Pozicie podla house.aseprite (pivot domu je dole v strede pri dverach).
            Point(root, "Okno L", h + new Vector3(-1.95f, 1.65f), WindowColor, 1.8f, 1.3f, 0.05f);
            Point(root, "Okno P", h + new Vector3(1.95f, 1.65f), WindowColor, 1.8f, 1.3f, 0.05f);
            Point(root, "Okno strecha", h + new Vector3(0f, 3.75f), WindowColor, 1.1f, 1f, 0.05f);
            Point(root, "Lampa nad dverami", h + new Vector3(0f, 1.6f), LanternColor, 6f, 1.2f, 0.08f);
        }
        var pot = Find(env, "CookingPot");
        if (pot != null) Point(root, "Ohen pod hrncom", pot.transform.position + new Vector3(0, 0.3f), FireColor, 4.5f, 1.4f, 0.3f);

        // Hrac nema vlastne svetlo (v noci je tma, svieti len dom a ohen). Stary lampas zo skorsej verzie zmaze.
        var player = GameObject.FindWithTag("Player");
        if (player != null)
            foreach (Transform t in player.transform.Cast<Transform>().Where(t => t.name == "Lampas").ToArray())
                Undo.DestroyObjectImmediate(t.gameObject);

        // ---- tiene: robi ich SpriteShadow na prefaboch. Stare Shadow Caster 2D zo sceny zmazeme.
        int removed = 0;
        foreach (var sc in Object.FindObjectsByType<ShadowCaster2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(sc)) continue; // v prefabe mapy uz nie su (stara scena)
            Undo.DestroyObjectImmediate(sc);
            removed++;
        }
        if (removed > 0) Debug.Log($"Osvetlenie: zmazanych {removed} Shadow Caster 2D (tiene teraz robi SpriteShadow).");
        FixSceneProps(scene);

        dn.SetNight(0f);
        // Az po SetNight: prefab ma farby svetiel ako cez den (preview = 0), takze v scene nevzniknu overrides.
        if (savePrefab) PlaceholderMapBuilder.SaveAsMapPrefab(root, PlaceholderMapBuilder.LightingPrefab);
        return dn;
    }

    // ---------------------------------------------------------------- stavanie

    static GameObject Point(GameObject root, string name, Vector3 pos, Color color, float radius, float intensity, float flicker)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        var light = go.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = color;
        light.pointLightInnerRadius = 0f;
        light.pointLightOuterRadius = radius;
        light.falloffIntensity = 0.65f;
        light.shadowsEnabled = false; // tiene robi SpriteShadow, nie Unity Shadow Caster 2D
        AllLayers(light);

        var warm = go.AddComponent<WarmLight>();
        var so = new SerializedObject(warm);
        so.FindProperty("intensity").floatValue = intensity;
        so.FindProperty("flicker").floatValue = flicker;
        so.ApplyModifiedPropertiesWithoutUndo();
        return go;
    }

    // Prefaby, ktore dostanu SpriteShadow (postavy, stromy, kamene a kriky ho uz maju).
    // noonRing = kruzok pod objektom len na poludnie, ked vrhany tien zmizne (cez den by bol druhy tien navyse).
    // Dom a ploty ho nemaju vobec, pod nimi by vyzeral divne.
    static readonly (string name, bool noonRing, int maxShadows)[] ShadowPrefabs =
    {
        ("House", false, 2), ("Chest", true, 2), ("CraftingTable", true, 2), ("CookingPot", true, 2),
        ("ScrapPile", true, 2), ("Mushroom", true, 1), ("GrassTuft", false, 1), ("Spikes", false, 1), ("Fence", false, 2),
        ("PenFence_H", false, 2), ("PenFence_V", false, 2), ("PenFence_H_Rail", false, 2), ("PenFence_V_Rail", false, 2),
        ("PenFence_Corner_BL", false, 2), ("PenFence_Corner_BR", false, 2), ("PenFence_Corner_TL", false, 2), ("PenFence_Corner_TR", false, 2),
    };

    /// Prida SpriteShadow na prefaby zo zoznamu. Existujuci nemeni (tvoje upravy v Inspectore ostanu).
    static int AddSpriteShadows()
    {
        int added = 0;
        foreach (var (name, noonRing, maxShadows) in ShadowPrefabs)
        {
            string path = $"Assets/Prefabs/Environment/{name}.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponent<SpriteShadow>() != null || asset.GetComponent<SpriteRenderer>() == null) continue;

            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                Configure(scope.prefabContentsRoot.AddComponent<SpriteShadow>(), noonRing, maxShadows);
            }
            added++;
        }
        return added;
    }

    static void Configure(SpriteShadow shadow, bool noonRing, int maxShadows)
    {
        var so = new SerializedObject(shadow);
        so.FindProperty("contactShadow").boolValue = noonRing;
        so.FindProperty("contactAlpha").floatValue = 0f; // cez den bez kruzku, len na poludnie (noonContactAlpha)
        so.FindProperty("maxShadows").intValue = maxShadows;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Dekoracie z generatora mapy (Props/*.aseprite, nie su to prefaby), podla mena spritu.
    // Ploche veci na zemi (kvety, klicky, pebbles - Tiles/Decor) tien nemaju.
    static readonly Dictionary<string, (bool noonRing, int maxShadows)> PropShadows = new Dictionary<string, (bool, int)>
    {
        ["trees_pine"] = (true, 2), ["trees_round"] = (true, 2), ["trees_tall"] = (true, 2),
        ["bushes"] = (true, 2), ["rocks"] = (true, 2), ["stumps"] = (true, 1),
        ["rocks_small"] = (false, 1), ["foxgloves"] = (false, 1),
    };

    /// Namalovane tiene pod stromami (Props/shadows) - nahradil ich SpriteShadow.
    public const string BakedShadowSprite = "Props/shadows";

    /// Ak dekoracia (strom, ker, kamen...) ma mat tien, prida jej SpriteShadow. Vola aj generator mapy.
    public static bool AddPropShadow(GameObject go, string spriteName)
    {
        if (!PropShadows.TryGetValue(spriteName, out var cfg) || go.GetComponent<SpriteShadow>() != null) return false;
        Configure(Undo.AddComponent<SpriteShadow>(go), cfg.noonRing, cfg.maxShadows); // Undo: Ctrl+Z vrati aj toto
        return true;
    }

    /// Dekoracie v starej scene (mapa este nie je prefab): zmaze namalovane tiene, ostatnym prida SpriteShadow.
    /// Mapa v prefabe ich uz ma z generatora, tam sa nic nemeni.
    static void FixSceneProps(UnityEngine.SceneManagement.Scene scene)
    {
        int removed = 0, added = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var props in root.GetComponentsInChildren<Transform>(true)
                     .Where(t => t.name == "Props" && !PrefabUtility.IsPartOfPrefabInstance(t)).ToArray())
        foreach (Transform t in props.Cast<Transform>().ToArray())
        {
            if (t.name == Path.GetFileName(BakedShadowSprite)) { Undo.DestroyObjectImmediate(t.gameObject); removed++; }
            else if (PropShadows.ContainsKey(t.name) && t.GetComponent<SpriteShadow>() == null)
            {
                if (AddPropShadow(t.gameObject, t.name)) added++;
            }
        }
        if (removed + added > 0)
            Debug.Log($"Osvetlenie: zmazanych {removed} namalovanych tienov, SpriteShadow pridany na {added} dekoracii.");
    }

    static void AllLayers(Light2D light) => light.targetSortingLayers = SortingLayer.layers.Select(l => l.id).ToArray();

    static GameObject Find(GameObject root, string name)
    {
        if (root == null) return null;
        var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        return t != null ? t.gameObject : null;
    }

    // ---------------------------------------------------------------- screenshoty (batchmode)

    public static void CapturePreview()
    {
        string outDir = Arg("-outDir") ?? Path.GetFullPath("Temp/LightingPreview");
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Farm.unity", OpenSceneMode.Single);
            var dn = Setup(savePrefab: false);

            var cam = Camera.main;
            var ppc = cam.GetComponent<PixelPerfectCamera>();
            if (ppc != null) ppc.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = 180f / 16f / 2f; // 320x180 px pri 16 PPU

            var env = EditorSceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == "Environment");
            var house = Find(env, "House");
            var player = GameObject.FindWithTag("Player");
            var shots = new (string name, Vector3 pos)[]
            {
                ("dom", house != null ? house.transform.position + new Vector3(-1f, 0.5f) : Vector3.zero),
                ("ohrada", new Vector3(20f, 15f)),
            };
            var times = new (string name, float night)[] { ("1_den", 0f), ("2_podvecer", 0.3f), ("3_zapad", 0.5f), ("4_sumrak", 0.75f), ("5_noc", 1f) };

            var lightUpdate = typeof(Light2D).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var (shot, pos) in shots)
            {
                if (player != null && shot == "dom") player.transform.position = pos + new Vector3(2.5f, -2f);
                if (player != null && shot == "ohrada") player.transform.position = pos + new Vector3(-3f, -1f);
                cam.transform.position = new Vector3(pos.x, pos.y, -10f);
                foreach (var (time, night) in times)
                {
                    dn.SetNight(night);
                    // V batchmode nebezi LateUpdate, svetla si mesh postavia az takto.
                    foreach (var l in Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None)) lightUpdate?.Invoke(l, null);
                    Save(cam, Path.Combine(outDir, $"{shot}_{time}.png"), 4);
                }
            }
            Debug.Log($"[LightingPreview] OK {outDir}");
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[LightingPreview] {ex}");
            EditorApplication.Exit(1);
        }
    }

    static void Save(Camera cam, string file, int scale)
    {
        const int w = 320, h = 180;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
        var old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = old;

        RenderTexture.active = rt;
        var src = new Texture2D(w, h, TextureFormat.RGB24, false);
        src.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        src.Apply();
        RenderTexture.active = null;
        rt.Release();

        var big = new Texture2D(w * scale, h * scale, TextureFormat.RGB24, false);
        var px = src.GetPixels32();
        var outPx = new Color32[w * scale * h * scale];
        for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                outPx[y * w * scale + x] = px[(y / scale) * w + x / scale];
        big.SetPixels32(outPx);
        File.WriteAllBytes(file, big.EncodeToPNG());
    }

    static string Arg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
