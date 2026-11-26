using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class BuildWheel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private static int closedOnFrame = -1;
    public static bool WasOpenThisFrame => IsOpen || closedOnFrame == Time.frameCount;

    [SerializeField] private BuildMode buildMode;
    [SerializeField] private List<DefenseData> options = new List<DefenseData>();
    [SerializeField] private float radius = 55f;
    [SerializeField] private Vector2 slotSize = new Vector2(58f, 18f);
    [SerializeField] private float deadZone = 15f;

    private InputAction buildAction;
    private Camera cam;
    private Transform player;
    private CanvasScaler scaler;
    private RectTransform wheel;
    private Text centerText;
    private List<Image> slotBorders = new List<Image>();
    private List<Text> slotTexts = new List<Text>();
    private int selectedIndex;
    private Vector2 lastMousePosition;

    void Start()
    {
        buildAction = InputSystem.actions.FindAction("Build");
        cam = Camera.main;

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
        }

        CreateWheel();
        SetOpen(false);
    }

    void OnDisable()
    {
        IsOpen = false;
    }

    void Update()
    {
        PixelUI.UpdateScale(scaler);

        if (Time.timeScale == 0f || LocationExit.Traveling) return;

        bool canOpen = buildMode.CanBuildHere() && options.Count > 0;
        if (IsOpen && !canOpen)
        {
            SetOpen(false);
        }

        if (canOpen && buildAction.WasPressedThisFrame())
        {
            SetOpen(!IsOpen);
            return;
        }

        if (!IsOpen) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
            return;
        }

        MoveWheelToPlayer();

        if (mouse != null)
        {
            Vector2 mousePosition = mouse.position.ReadValue();
            if (mousePosition != lastMousePosition)
            {
                SelectFromMouse(mousePosition);
                lastMousePosition = mousePosition;
            }
        }

        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
        {
            selectedIndex = (selectedIndex + 1) % options.Count;
        }

        RefreshSlots();

        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            PickSelected();
        }
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        wheel.gameObject.SetActive(open);

        if (!open)
        {
            closedOnFrame = Time.frameCount;
        }
    }

    private void PickSelected()
    {
        DefenseData choice = options[selectedIndex];
        if (choice == null || choice.Prefab == null) return;

        SetOpen(false);
        buildMode.StartBuilding(choice);
    }

    private void MoveWheelToPlayer()
    {
        if (player == null) return;

        Vector3 screenPosition = cam.WorldToScreenPoint(player.position + Vector3.up);
        wheel.anchoredPosition = PixelUI.ScreenToCanvas(screenPosition, scaler);
    }

    private void SelectFromMouse(Vector2 mouseScreenPosition)
    {
        Vector2 mouse = PixelUI.ScreenToCanvas(mouseScreenPosition, scaler);
        Vector2 direction = mouse - wheel.anchoredPosition;
        if (direction.magnitude < deadZone) return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float angleFromTop = Mathf.Repeat(90f - angle, 360f);
        float slotAngle = 360f / options.Count;

        selectedIndex = Mathf.RoundToInt(angleFromTop / slotAngle) % options.Count;
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < options.Count; i++)
        {
            DefenseData option = options[i];
            bool available = option != null && option.Prefab != null;

            if (option == null)
            {
                slotTexts[i].text = "?";
            }
            else
            {
                slotTexts[i].text = option.DisplayName;
            }

            if (available)
            {
                slotTexts[i].color = Color.white;
            }
            else
            {
                slotTexts[i].color = Color.gray;
            }

            if (i == selectedIndex)
            {
                slotBorders[i].color = PixelUI.AccentColor;
            }
            else
            {
                slotBorders[i].color = PixelUI.BorderColor;
            }
        }

        DefenseData selected = options[selectedIndex];
        if (selected == null)
        {
            centerText.text = "Empty slot";
        }
        else if (selected.Prefab == null)
        {
            centerText.text = selected.DisplayName + "\nnot in game yet";
        }
        else
        {
            centerText.text = selected.DisplayName + "\nclick to build";
        }
    }

    private void CreateWheel()
    {
        Canvas canvas = PixelUI.CreateCanvas("Build Wheel", 95, out scaler);
        canvas.transform.SetParent(transform, false);

        wheel = PixelUI.NewRect("Wheel", canvas.transform);
        wheel.anchorMin = Vector2.zero;
        wheel.anchorMax = Vector2.zero;
        wheel.sizeDelta = Vector2.zero;

        centerText = PixelUI.NewText("Center", wheel, 8, TextAnchor.MiddleCenter);

        float slotAngle = 360f / Mathf.Max(1, options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            float angle = (90f - i * slotAngle) * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            RectTransform slot = PixelUI.NewBox("Slot " + i, wheel);
            slot.sizeDelta = slotSize;
            slot.anchoredPosition = offset;

            Text label = PixelUI.NewText("Label", slot, 8, TextAnchor.MiddleCenter);
            PixelUI.Stretch(label.rectTransform, 2);

            slotBorders.Add(slot.GetComponent<Image>());
            slotTexts.Add(label);
        }
    }
}