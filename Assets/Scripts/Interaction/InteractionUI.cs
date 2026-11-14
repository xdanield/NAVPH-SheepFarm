using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI pre interakciu, postavi sa samo pri spusteni hry:
///  - napoveda nad vybranym objektom ("E  Chop tree", cervene "Inventory full")
///  - progress bar pod napovedou, kym hrac drzi E
///  - vyskakujuce texty "+3 Wood" s ikonkou
/// Bezi az po kamere (LateUpdate s neskorsim poradim), aby napoveda neposkakovala.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerInteractor))]
public class InteractionUI : MonoBehaviour
{
    [SerializeField] private int barHeight = 3;
    [SerializeField] private float popupDuration = 1.1f;
    [SerializeField] private float popupRise = 16f;

    private PlayerInteractor interactor;
    private Camera cam;
    private CanvasScaler scaler;
    private RectTransform root;

    private RectTransform prompt;
    private Text promptText;
    private RectTransform bar;
    private RectTransform barFill;

    private readonly List<Popup> popups = new List<Popup>();

    private class Popup
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Vector3 world;
        public float age;
        public float stackOffset;
    }

    void Start()
    {
        interactor = GetComponent<PlayerInteractor>();
        var canvas = PixelUI.CreateCanvas("Interaction UI", 90, out scaler);
        root = (RectTransform)canvas.transform;
        BuildPrompt();
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        PixelUI.UpdateScale(scaler);
        UpdatePrompt();
        UpdatePopups();
    }

    // ------------------------------------------------------------ napoveda

    private void BuildPrompt()
    {
        prompt = PixelUI.NewBox("Prompt", root);
        prompt.anchorMin = prompt.anchorMax = Vector2.zero;
        prompt.pivot = new Vector2(0.5f, 0f);

        promptText = PixelUI.NewText("Text", prompt, 8, TextAnchor.MiddleCenter);
        PixelUI.Stretch(promptText.rectTransform, 2);

        bar = PixelUI.NewImage("Progress", prompt, PixelUI.BorderColor).rectTransform;
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(0f, -barHeight - 2);
        bar.offsetMax = new Vector2(0f, -1f);

        var barBg = PixelUI.NewImage("Background", bar, PixelUI.PanelColor).rectTransform;
        PixelUI.Stretch(barBg, 1);
        barFill = PixelUI.NewImage("Fill", barBg, PixelUI.AccentColor).rectTransform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0f, 1f);
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;

        prompt.gameObject.SetActive(false);
    }

    private void UpdatePrompt()
    {
        var target = interactor.Target;
        bool show = target != null && !InventoryUI.IsOpen;
        prompt.gameObject.SetActive(show);
        if (!show) return;

        string blocked = interactor.BlockedReason;
        promptText.text = blocked != null
            ? $"<color=#{ColorUtility.ToHtmlStringRGB(PixelUI.WarningColor)}>{blocked}</color>"
            : $"<color=#{ColorUtility.ToHtmlStringRGB(PixelUI.AccentColor)}>{interactor.KeyLabel}</color>  {target.ActionName}";

        var size = new Vector2(Mathf.Ceil(promptText.preferredWidth) + 8, Mathf.Ceil(promptText.preferredHeight) + 4);
        prompt.sizeDelta = size;
        prompt.anchoredPosition = ClampToScreen(WorldToCanvas(target.PromptPosition), size);

        float progress = interactor.HoldProgress;
        bar.gameObject.SetActive(progress > 0f);
        barFill.anchorMax = new Vector2(progress, 1f);
    }

    // ------------------------------------------------------------ vyskakujuce texty

    public void ShowPopup(Vector3 worldPosition, string text, Sprite icon, Color color)
    {
        if (root == null) return;

        var rect = PixelUI.NewRect("Popup", root);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0f);
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var label = PixelUI.NewText("Text", rect, 8, TextAnchor.MiddleLeft);
        label.text = text;
        label.color = color;
        float textWidth = Mathf.Ceil(label.preferredWidth);
        const float iconSize = 10f, gap = 2f;
        float width = textWidth + (icon != null ? iconSize + gap : 0f);
        rect.sizeDelta = new Vector2(width, 12f);

        float x = 0f;
        if (icon != null)
        {
            var image = PixelUI.NewImage("Icon", rect, Color.white);
            image.sprite = icon;
            image.preserveAspect = true;
            PixelUI.SetBottomLeft(image.rectTransform, new Vector2(0f, 1f), new Vector2(iconSize, iconSize));
            x = iconSize + gap;
        }
        PixelUI.SetBottomLeft(label.rectTransform, new Vector2(x, 0f), new Vector2(textWidth, 12f));

        // Viac textov naraz (napr. "+3 Wood" a "Inventory full") sa uklada nad seba.
        int alive = 0;
        foreach (var p in popups) if (p.age < 0.4f) alive++;

        popups.Add(new Popup { rect = rect, group = group, world = worldPosition, stackOffset = alive * 11f });
    }

    private void UpdatePopups()
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            p.age += Time.deltaTime;
            float t = p.age / popupDuration;
            if (t >= 1f)
            {
                Destroy(p.rect.gameObject);
                popups.RemoveAt(i);
                continue;
            }

            float rise = (1f - (1f - t) * (1f - t)) * popupRise; // rychlo hore, potom spomali
            Vector2 pos = WorldToCanvas(p.world) + new Vector2(0f, 12f + p.stackOffset + rise);
            p.rect.anchoredPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
            p.group.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        }
    }

    // ------------------------------------------------------------ pomocne

    private Vector2 WorldToCanvas(Vector3 world)
    {
        Vector2 screen = cam.WorldToScreenPoint(world);
        Vector2 local = PixelUI.ScreenToCanvas(screen, scaler);
        return new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
    }

    private Vector2 ClampToScreen(Vector2 pos, Vector2 size)
    {
        Vector2 canvasSize = root.rect.size;
        pos.x = Mathf.Clamp(pos.x, size.x / 2f + 2f, canvasSize.x - size.x / 2f - 2f);
        pos.y = Mathf.Clamp(pos.y, barHeight + 4f, canvasSize.y - size.y - 2f);
        return pos;
    }
}
