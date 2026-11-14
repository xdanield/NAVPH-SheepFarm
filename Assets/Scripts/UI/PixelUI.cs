using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pomocne funkcie na stavbu UI z kodu (inventar, napovedy pri zbere...).
/// Rozmery su v UI pixeloch: obrazovka ma 640x360 (2x hra, ktora ma 320x180) a UI sa zvacsuje celym cislom
/// (1080p = 3x, 1440p = 4x), takze pixely su ostre. Hra je na 1080p 6x, UI ma teda 2x jemnejsie pixely.
/// </summary>
public static class PixelUI
{
    public const int ReferenceHeight = 360;

    public static readonly Color PanelColor = new Color32(0x1d, 0x1a, 0x24, 0xf0);
    public static readonly Color BorderColor = new Color32(0x5a, 0x4e, 0x6b, 0xff);
    public static readonly Color AccentColor = new Color32(0xf2, 0xc1, 0x4e, 0xff);
    public static readonly Color WarningColor = new Color32(0xff, 0x6b, 0x5a, 0xff);

    private static Font font;
    public static Font Font
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static Canvas CreateCanvas(string name, int sortingOrder, out CanvasScaler scaler)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvas.pixelPerfect = true;
        scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        UpdateScale(scaler);
        return canvas;
    }

    /// <summary>Volaj kazdy frame, aby UI sedelo aj po zmene velkosti okna.</summary>
    public static void UpdateScale(CanvasScaler scaler)
    {
        float scale = Mathf.Max(1, Mathf.Floor(Screen.height / (float)ReferenceHeight));
        if (!Mathf.Approximately(scaler.scaleFactor, scale)) scaler.scaleFactor = scale;
    }

    /// <summary>Pozicia na obrazovke (px) -> pozicia v canvase s anchor vlavo dole.</summary>
    public static Vector2 ScreenToCanvas(Vector2 screen, CanvasScaler scaler) => screen / scaler.scaleFactor;

    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image NewImage(string name, Transform parent, Color color)
    {
        var image = NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static Text NewText(string name, Transform parent, int size, TextAnchor alignment)
    {
        var text = NewRect(name, parent).gameObject.AddComponent<Text>();
        text.font = Font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.8f);
        shadow.effectDistance = new Vector2(1, -1);
        return text;
    }

    /// <summary>Ramik s pozadim (ako tooltip). Vrati vonkajsi ramik, pozadie je jeho dieta.</summary>
    public static RectTransform NewBox(string name, Transform parent)
    {
        var border = NewImage(name, parent, BorderColor).rectTransform;
        Stretch(NewImage("Background", border, PanelColor).rectTransform, 1);
        return border;
    }

    public static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public static void SetBottomLeft(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }
}
