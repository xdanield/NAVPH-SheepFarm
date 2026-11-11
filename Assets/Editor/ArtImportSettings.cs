using UnityEditor;
using UnityEditor.U2D.Aseprite;
using UnityEngine;

/// <summary>
/// Automaticky nastavi import grafiky v Assets/Art pre pixelart:
/// 32 PPU, Point filter, bez kompresie, pivot dole v strede (tiles v strede).
/// Plati len pre nove subory; pre existujuce: menu Baa-ricade > Opravit import grafiky.
/// </summary>
public class ArtImportSettings : AssetPostprocessor
{
    public const int PPU = 32; // 1 tile = 32 px = 1 Unity unit
    const string ArtRoot = "Assets/Art/";

    void OnPreprocessAsset()
    {
        if (!assetPath.StartsWith(ArtRoot) || !assetImporter.importSettingsMissing) return;

        if (assetImporter is AsepriteImporter aseprite)
            Apply(aseprite);
        else if (assetImporter is TextureImporter texture)
            Apply(texture);
    }

    static bool IsTile(string path) => path.Contains("/Tiles/");
    static bool IsCharacter(string path) => path.Contains("/Characters/");

    static void Apply(AsepriteImporter importer)
    {
        string path = importer.assetPath;
        importer.spritePixelsPerUnit = PPU;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.layerImportMode = LayerImportModes.MergeFrame;
        importer.pivotSpace = PivotSpaces.Canvas;
        importer.pivotAlignment = IsTile(path) ? SpriteAlignment.Center : SpriteAlignment.BottomCenter;

        bool character = IsCharacter(path);
        importer.importMode = character ? FileImportModes.AnimatedSprite : FileImportModes.SpriteSheet;
        importer.generateAnimationClips = character;
        importer.generateModelPrefab = character;

        importer.SetImporterPlatformSettings(Uncompressed());
    }

    static void Apply(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PPU;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)(IsTile(importer.assetPath) ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }

    static TextureImporterPlatformSettings Uncompressed() => new TextureImporterPlatformSettings
    {
        name = "DefaultTexturePlatform",
        overridden = false,
        maxTextureSize = 2048,
        format = TextureImporterFormat.Automatic,
        textureCompression = TextureImporterCompression.Uncompressed,
    };

    [MenuItem("Baa-ricade/Opravit import grafiky")]
    public static void FixAll()
    {
        foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/Art" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path);
            if (importer is AsepriteImporter aseprite) Apply(aseprite);
            else if (importer is TextureImporter texture) Apply(texture);
            else continue;
            importer.SaveAndReimport();
        }
        Debug.Log("Import grafiky v Assets/Art nastaveny (32 PPU, Point, bez kompresie).");
    }
}
