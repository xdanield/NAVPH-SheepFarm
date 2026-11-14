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
///     - svetla v oknach domu, lampas pri dverach, ohen pod hrncom, lampas hracovi,
///     - Shadow Caster 2D na vsetko, co ma collider (stromy, kamene, dom, plot...), tien sa hadze od spodku objektu.
///     Da sa spustit znova, stare svetla zmaze a postavi nanovo.
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
        Setup();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Osvetlenie hotove. Cas posuvas na objekte Lighting (Nahlad), v hre drz T na zrychlenie casu. Nezabudni ulozit scenu (Ctrl+S).");
    }

    public static DayNightLighting Setup()
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
        so.FindProperty("cycle").objectReferenceValue = Object.FindAnyObjectByType<DayNightCycle>();
        so.FindProperty("globalLight").objectReferenceValue = global;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---- teple svetla na farme
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Environment");
        var house = Find(env, "House");
        if (house != null)
        {
            var h = house.transform.position;
            // Pozicie podla house.aseprite (pivot domu je dole v strede pri dverach).
            Point(root, "Okno L", h + new Vector3(-1.95f, 1.65f), WindowColor, 1.8f, 1.3f, 0.05f, false);
            Point(root, "Okno P", h + new Vector3(1.95f, 1.65f), WindowColor, 1.8f, 1.3f, 0.05f, false);
            Point(root, "Okno strecha", h + new Vector3(0f, 3.75f), WindowColor, 1.1f, 1f, 0.05f, false);
            Point(root, "Lampa nad dverami", h + new Vector3(0f, 1.6f), LanternColor, 6f, 1.2f, 0.08f, true);
        }
        var pot = Find(env, "CookingPot");
        if (pot != null) Point(root, "Ohen pod hrncom", pot.transform.position + new Vector3(0, 0.3f), FireColor, 4.5f, 1.4f, 0.3f, true);

        // Lampas hraca (dieta hraca, aby chodil s nim).
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            foreach (Transform t in player.transform.Cast<Transform>().Where(t => t.name == "Lampas").ToArray())
                Undo.DestroyObjectImmediate(t.gameObject);
            var l = Point(root, "Lampas", player.transform.position + new Vector3(0, 0.5f), LanternColor, 3.5f, 0.8f, 0.06f, true);
            Undo.SetTransformParent(l.transform, player.transform, "Lampas");
        }

        // ---- tiene
        int casters = 0;
        if (env != null)
            foreach (var box in env.GetComponentsInChildren<BoxCollider2D>(true))
                if (!box.isTrigger && box.GetComponent<SpriteRenderer>() != null) { ShadowCaster(box); casters++; }

        dn.SetNight(0f);
        Debug.Log($"Osvetlenie: {casters} objektov hadze tien.");
        return dn;
    }

    // ---------------------------------------------------------------- stavanie

    static GameObject Point(GameObject root, string name, Vector3 pos, Color color, float radius, float intensity, float flicker, bool shadows)
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
        light.shadowsEnabled = shadows;
        light.shadowIntensity = 0.8f;
        light.shadowSoftness = 0.25f;
        AllLayers(light);

        var warm = go.AddComponent<WarmLight>();
        var so = new SerializedObject(warm);
        so.FindProperty("intensity").floatValue = intensity;
        so.FindProperty("flicker").floatValue = flicker;
        so.ApplyModifiedPropertiesWithoutUndo();
        return go;
    }

    /// Tien sa hadze od obrysu collidera (spodok objektu), nie od celeho spritu, lebo pozerame zhora.
    static void ShadowCaster(BoxCollider2D box)
    {
        var go = box.gameObject;
        var sc = go.GetComponent<ShadowCaster2D>();
        if (sc == null) sc = Undo.AddComponent<ShadowCaster2D>(go);
        sc.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;

        Vector2 o = box.offset, e = box.size * 0.5f;
        var corners = new[]
        {
            new Vector3(o.x - e.x, o.y - e.y), new Vector3(o.x + e.x, o.y - e.y),
            new Vector3(o.x + e.x, o.y + e.y), new Vector3(o.x - e.x, o.y + e.y),
        };

        var so = new SerializedObject(sc);
        so.FindProperty("m_ShadowCastingSource").intValue = 1; // ShapeEditor
        var path = so.FindProperty("m_ShapePath");
        path.arraySize = corners.Length;
        for (int i = 0; i < corners.Length; i++) path.GetArrayElementAtIndex(i).vector3Value = corners[i];
        int hash = 17;
        foreach (var c in corners) hash = hash * 31 + c.GetHashCode();
        so.FindProperty("m_ShapePathHash").intValue = hash == 0 ? 1 : hash;
        var layers = so.FindProperty("m_ApplyToSortingLayers");
        var ids = SortingLayer.layers.Select(l => l.id).ToArray();
        layers.arraySize = ids.Length;
        for (int i = 0; i < ids.Length; i++) layers.GetArrayElementAtIndex(i).intValue = ids[i];
        so.ApplyModifiedProperties();
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
            var dn = Setup();

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

            var update = typeof(ShadowCaster2D).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var lightUpdate = typeof(Light2D).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var (shot, pos) in shots)
            {
                if (player != null && shot == "dom") player.transform.position = pos + new Vector3(2.5f, -2f);
                if (player != null && shot == "ohrada") player.transform.position = pos + new Vector3(-3f, -1f);
                cam.transform.position = new Vector3(pos.x, pos.y, -10f);
                foreach (var (time, night) in times)
                {
                    dn.SetNight(night);
                    // V batchmode nebezi Update/LateUpdate, svetla a tiene si mesh postavia az takto.
                    foreach (var sc in Object.FindObjectsByType<ShadowCaster2D>(FindObjectsSortMode.None)) update?.Invoke(sc, null);
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
