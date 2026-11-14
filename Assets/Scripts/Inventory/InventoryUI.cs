using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Inventar v style Core Keeper. UI sa postavi samo pri spusteni hry.
///
/// Zatvoreny:  hotbar dole v strede. 1-6, koliesko mysi alebo klik na slot = vyber slotu.
/// Koliesko meni vybrany slot aj v otvorenom inventari.
/// Otvoreny (I): panel s batohom a hotbarom v strede obrazovky, hra je pozastavena.
///     Lavy klik        zobrat / polozit / spojit / vymenit stack
///     Pravy klik       zobrat polovicu / polozit jeden kus
///     Shift + lavy     presunut medzi hotbarom a batohom
///     I alebo Esc      zatvorit (co drzis na kurzore, vrati sa do inventara)
///
/// Rozmery su v hernych pixeloch (obrazovka 640x360, hra ma 320x180), UI sa zvacsuje celym cislom.
/// </summary>
[RequireComponent(typeof(Inventory))]
public class InventoryUI : MonoBehaviour
{
    [Header("Rozmery (herne pixely)")]
    [SerializeField] private int slotSize = 24;
    [SerializeField] private int slotGap = 2;
    [SerializeField] private int iconSize = 18;
    [SerializeField] private int padding = 5;
    [SerializeField] private int hotbarBottomMargin = 6;

    [Header("Farby")]
    [SerializeField] private Color panelColor = new Color32(0x1d, 0x1a, 0x24, 0xf0);
    [SerializeField] private Color panelBorderColor = new Color32(0x5a, 0x4e, 0x6b, 0xff);
    [SerializeField] private Color slotColor = new Color32(0x2b, 0x26, 0x35, 0xe6);
    [SerializeField] private Color slotHoverColor = new Color32(0x45, 0x3d, 0x55, 0xf0);
    [SerializeField] private Color slotBorderColor = new Color32(0x4a, 0x42, 0x58, 0xff);
    [SerializeField] private Color selectedBorderColor = new Color32(0xf2, 0xc1, 0x4e, 0xff);

    /// <summary>Je otvoreny inventar? Ostatne skripty (utok, stavanie) maju vtedy ignorovat vstup.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Je mys nad inventarom alebo hotbarom? Utok lavym klikom ma vtedy nic nerobit.</summary>
    public static bool PointerOverUI { get; private set; }

    private Inventory inventory;
    private Canvas canvas;
    private CanvasScaler scaler;
    private RectTransform root;
    private RectTransform hud;
    private RectTransform panel;
    private SlotView[] hudSlots;
    private SlotView[] panelSlots;
    private Image handIcon;
    private Text handCount;
    private RectTransform tooltip;
    private Text tooltipText;

    private ItemStack hand;
    private int hoveredIndex = -1;
    private float timeScaleBeforeOpen = 1f;

    private class SlotView
    {
        public int index;
        public RectTransform rect;
        public Image border;
        public Image background;
        public Image icon;
        public Text count;
    }

    void Start()
    {
        inventory = GetComponent<Inventory>();
        BuildCanvas();
        BuildHud();
        BuildPanel();
        BuildHandAndTooltip();
        SetOpen(false);
    }

    void OnDestroy()
    {
        if (IsOpen) Time.timeScale = timeScaleBeforeOpen;
        IsOpen = false;
        PointerOverUI = false;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void Update()
    {
        PixelUI.UpdateScale(scaler);
        HandleInput();
        Refresh();
    }

    // ------------------------------------------------------------ vstup

    private void HandleInput()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null) return;

        if (keyboard.iKey.wasPressedThisFrame) SetOpen(!IsOpen);
        else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);

        for (int i = 0; i < inventory.HotbarSize && i < 10; i++)
        {
            Key key = i < 9 ? Key.Digit1 + i : Key.Digit0;
            if (keyboard[key].wasPressedThisFrame) inventory.Select(i);
        }

        hoveredIndex = -1;
        PointerOverUI = false;
        if (mouse == null) return;
        Vector2 pointer = mouse.position.ReadValue();

        // Koliesko meni vybrany slot v hotbare, ci je inventar otvoreny alebo nie.
        float scroll = mouse.scroll.ReadValue().y;
        if (scroll > 0.01f) inventory.SelectNext(-1);
        else if (scroll < -0.01f) inventory.SelectNext(1);

        var views = IsOpen ? panelSlots : hudSlots;
        foreach (var view in views)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(view.rect, pointer, null))
            {
                hoveredIndex = view.index;
                break;
            }
        }
        var area = IsOpen ? panel : hud;
        PointerOverUI = IsOpen || RectTransformUtility.RectangleContainsScreenPoint(area, pointer, null);

        if (!IsOpen)
        {
            // Zatvoreny inventar: klik na slot v hotbare ho vyberie.
            if (hoveredIndex >= 0 && mouse.leftButton.wasPressedThisFrame) inventory.Select(hoveredIndex);
            return;
        }

        if (hoveredIndex < 0) return;

        bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (shift && hand.IsEmpty) inventory.QuickMove(hoveredIndex);
            else hand = inventory.PrimaryClick(hoveredIndex, hand);
        }
        else if (mouse.rightButton.wasPressedThisFrame)
        {
            hand = inventory.SecondaryClick(hoveredIndex, hand);
        }
    }

    private void SetOpen(bool open)
    {
        if (open == IsOpen && panel.gameObject.activeSelf == open) return;

        if (open)
        {
            timeScaleBeforeOpen = Time.timeScale;
            Time.timeScale = 0f; // GDD: kym je otvorene menu, cas stoji
        }
        else
        {
            if (!hand.IsEmpty)
            {
                int left = inventory.Return(hand);
                if (left > 0) Debug.LogWarning($"Inventar: {left}x {hand.item.DisplayName} sa nevratilo do inventara.");
                hand = ItemStack.Empty;
            }
            if (IsOpen) Time.timeScale = timeScaleBeforeOpen;
        }

        IsOpen = open;
        panel.gameObject.SetActive(open);
        hud.gameObject.SetActive(!open);
        hoveredIndex = -1;
    }

    // ------------------------------------------------------------ vykreslovanie

    private void Refresh()
    {
        foreach (var view in hudSlots) RefreshSlot(view);
        if (IsOpen) foreach (var view in panelSlots) RefreshSlot(view);

        Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Vector2 local = PixelUI.ScreenToCanvas(pointer, scaler);

        handIcon.gameObject.SetActive(IsOpen && !hand.IsEmpty);
        if (!hand.IsEmpty)
        {
            handIcon.sprite = hand.item.Icon;
            handIcon.rectTransform.anchoredPosition = local;
            handCount.text = hand.count > 1 ? hand.count.ToString() : "";
        }

        bool showTooltip = IsOpen && hand.IsEmpty && hoveredIndex >= 0 && !inventory.Get(hoveredIndex).IsEmpty;
        tooltip.gameObject.SetActive(showTooltip);
        if (showTooltip)
        {
            var item = inventory.Get(hoveredIndex).item;
            tooltipText.text = string.IsNullOrEmpty(item.Description)
                ? item.DisplayName
                : $"{item.DisplayName}\n<size=7><color=#b8b0c8>{item.Description}</color></size>";
            var size = new Vector2(tooltipText.preferredWidth + 8, tooltipText.preferredHeight + 6);
            tooltip.sizeDelta = size;
            float maxX = root.rect.width - size.x;
            tooltip.anchoredPosition = new Vector2(Mathf.Min(local.x + 8, maxX), local.y - 8);
        }
    }

    private void RefreshSlot(SlotView view)
    {
        var stack = inventory.Get(view.index);
        bool selected = inventory.IsHotbar(view.index) && view.index == inventory.SelectedIndex;

        view.border.color = selected ? selectedBorderColor : slotBorderColor;
        view.background.color = view.index == hoveredIndex ? slotHoverColor : slotColor;
        view.icon.enabled = !stack.IsEmpty && stack.item.Icon != null;
        if (!stack.IsEmpty) view.icon.sprite = stack.item.Icon;
        view.count.text = stack.count > 1 ? stack.count.ToString() : "";
    }

    // ------------------------------------------------------------ stavba UI

    private void BuildCanvas()
    {
        canvas = PixelUI.CreateCanvas("Inventory UI", 100, out scaler);
        root = (RectTransform)canvas.transform;
    }

    private void BuildHud()
    {
        int n = inventory.HotbarSize;
        float width = RowWidth(n) + 2 * slotGap;
        float height = slotSize + 2 * slotGap;

        hud = PixelUI.NewRect("Hotbar", root);
        hud.anchorMin = hud.anchorMax = new Vector2(0.5f, 0f);
        hud.pivot = new Vector2(0.5f, 0f);
        hud.anchoredPosition = new Vector2(0, hotbarBottomMargin);
        hud.sizeDelta = new Vector2(width, height);

        hudSlots = new SlotView[n];
        for (int i = 0; i < n; i++)
            hudSlots[i] = CreateSlot(hud, i, new Vector2(slotGap + i * (slotSize + slotGap), slotGap), true);
    }

    private void BuildPanel()
    {
        int cols = inventory.Columns;
        int rows = inventory.BagRows;
        const int titleHeight = 12;
        int separator = rows > 0 ? 6 : 0;
        float gridWidth = RowWidth(cols);
        float bagHeight = rows > 0 ? rows * slotSize + (rows - 1) * slotGap : 0;

        var border = PixelUI.NewImage("Inventory Panel", root, panelBorderColor);
        panel = border.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(gridWidth + 2 * padding, padding + slotSize + separator + bagHeight + titleHeight + padding);
        PixelUI.Stretch(PixelUI.NewImage("Background", panel, panelColor).rectTransform, 1);

        var title = PixelUI.NewText("Title", panel, 9, TextAnchor.MiddleLeft);
        title.text = "Inventory";
        title.rectTransform.anchorMin = new Vector2(0, 1);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.pivot = new Vector2(0.5f, 1);
        title.rectTransform.offsetMin = new Vector2(padding + 1, -padding - titleHeight);
        title.rectTransform.offsetMax = new Vector2(-padding, -padding);

        panelSlots = new SlotView[inventory.SlotCount];

        // Hotbar je spodny riadok panela, ako v Core Keeper.
        for (int c = 0; c < cols; c++)
            panelSlots[c] = CreateSlot(panel, c, new Vector2(padding + c * (slotSize + slotGap), padding), true);

        if (rows > 0)
        {
            var line = PixelUI.NewImage("Separator", panel, panelBorderColor).rectTransform;
            PixelUI.SetBottomLeft(line, new Vector2(padding, padding + slotSize + separator / 2f - 0.5f), new Vector2(gridWidth, 1));
        }

        // Batoh nad hotbarom, prvy riadok batohu je hore.
        for (int r = 0; r < rows; r++)
        {
            float y = padding + slotSize + separator + (rows - 1 - r) * (slotSize + slotGap);
            for (int c = 0; c < cols; c++)
            {
                int index = cols + r * cols + c;
                panelSlots[index] = CreateSlot(panel, index, new Vector2(padding + c * (slotSize + slotGap), y), false);
            }
        }
    }

    private void BuildHandAndTooltip()
    {
        handIcon = PixelUI.NewImage("Held Item", root, Color.white);
        handIcon.preserveAspect = true;
        var rt = handIcon.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(iconSize, iconSize);
        handCount = PixelUI.NewText("Count", rt, 8, TextAnchor.LowerRight);
        PixelUI.Stretch(handCount.rectTransform, -3);

        var bg = PixelUI.NewImage("Tooltip", root, panelBorderColor);
        tooltip = bg.rectTransform;
        tooltip.anchorMin = tooltip.anchorMax = Vector2.zero;
        tooltip.pivot = new Vector2(0, 1);
        PixelUI.Stretch(PixelUI.NewImage("Background", tooltip, panelColor).rectTransform, 1);
        tooltipText = PixelUI.NewText("Text", tooltip, 8, TextAnchor.MiddleLeft);
        tooltipText.horizontalOverflow = HorizontalWrapMode.Overflow;
        tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
        PixelUI.Stretch(tooltipText.rectTransform, 4);
    }

    private SlotView CreateSlot(RectTransform parent, int index, Vector2 position, bool showKey)
    {
        var view = new SlotView { index = index };
        view.border = PixelUI.NewImage($"Slot {index}", parent, slotBorderColor);
        view.rect = view.border.rectTransform;
        PixelUI.SetBottomLeft(view.rect, position, new Vector2(slotSize, slotSize));

        view.background = PixelUI.NewImage("Background", view.rect, slotColor);
        PixelUI.Stretch(view.background.rectTransform, 1);

        view.icon = PixelUI.NewImage("Icon", view.rect, Color.white);
        view.icon.preserveAspect = true;
        var iconRect = view.icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);

        view.count = PixelUI.NewText("Count", view.rect, 8, TextAnchor.LowerRight);
        PixelUI.Stretch(view.count.rectTransform, 2);

        if (showKey && index < 10)
        {
            var key = PixelUI.NewText("Key", view.rect, 6, TextAnchor.UpperLeft);
            key.text = ((index + 1) % 10).ToString();
            key.color = new Color(1, 1, 1, 0.55f);
            PixelUI.Stretch(key.rectTransform, 2);
        }
        return view;
    }

    // ------------------------------------------------------------ pomocne

    private float RowWidth(int n) => n * slotSize + (n - 1) * slotGap;
}
