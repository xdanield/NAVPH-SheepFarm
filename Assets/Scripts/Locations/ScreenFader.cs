using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cierna clona cez celu obrazovku (presun medzi lokaciami) a nazov lokacie po prichode.
/// Netreba ho davat do sceny, vytvori sa sam pri prvom pouziti (ScreenFader.Instance).
/// </summary>
public class ScreenFader : MonoBehaviour
{
    private static ScreenFader instance;

    private CanvasScaler scaler;
    private Image black;
    private Text title;
    private float titleTime, titleDuration;

    public static ScreenFader Instance
    {
        get
        {
            if (instance == null) instance = new GameObject("ScreenFader").AddComponent<ScreenFader>();
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        var canvas = PixelUI.CreateCanvas("FaderCanvas", 900, out scaler); // nad inventarom aj napovedami
        canvas.transform.SetParent(transform, false);

        black = PixelUI.NewImage("Black", canvas.transform, Color.black);
        PixelUI.Stretch(black.rectTransform, 0);
        SetAlpha(black, 0f);

        title = PixelUI.NewText("Title", canvas.transform, 16, TextAnchor.MiddleCenter);
        var rt = title.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -48);
        rt.sizeDelta = new Vector2(400, 24);
        title.color = PixelUI.AccentColor;
        SetAlpha(title, 0f);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        PixelUI.UpdateScale(scaler);

        if (titleTime < titleDuration)
        {
            titleTime += Time.unscaledDeltaTime;
            // rychlo sa objavi, chvilu drzi, pomaly zmizne
            float t = titleTime / titleDuration;
            float alpha = t < 0.15f ? t / 0.15f : t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
            SetAlpha(title, Mathf.Clamp01(alpha));
        }
    }

    /// <summary>Plynule na alpha (1 = cierna, 0 = nic). Bezi aj pocas pauzy (unscaled time).</summary>
    public IEnumerator Fade(float to, float duration)
    {
        float from = black.color.a;
        if (duration <= 0f) { SetAlpha(black, to); yield break; }
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            SetAlpha(black, Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetAlpha(black, to);
    }

    /// <summary>Nazov lokacie hore na obrazovke na par sekund.</summary>
    public void ShowTitle(string text, float duration = 2.5f)
    {
        if (string.IsNullOrEmpty(text)) return;
        title.text = text;
        titleTime = 0f;
        titleDuration = duration;
        SetAlpha(title, 0f);
    }

    private static void SetAlpha(Graphic g, float a)
    {
        var c = g.color;
        c.a = a;
        g.color = c;
    }
}
