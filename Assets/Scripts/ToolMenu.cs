using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Werkzeug-Menü für VR: Ein Panel am linken Controller/Handgelenk mit Buttons.
/// Man zeigt mit dem rechten Controller darauf (Laser) und drückt den Trigger.
///
/// Das komplette Menü (Canvas, Buttons, Laser, Zeiger) wird zur Laufzeit per Code
/// erzeugt – im Editor muss nur dieses Skript an ein GameObject gehängt und das
/// Leveler-Prefab zugewiesen werden. Die Controller-Anker werden automatisch vom
/// OVRCameraRig geholt.
/// </summary>
public class ToolMenu : MonoBehaviour
{
    [Header("Spawn")]
    [Tooltip("Das Prefab, das über den Button 'Leveler +' erzeugt wird.")]
    [SerializeField] private GameObject levelerPrefab;
    [Tooltip("Abstand vor dem rechten Controller, in dem ein Werkzeug gespawnt wird (Meter).")]
    [SerializeField] private float spawnDistance = 0.4f;

    [Header("Zeigen & Auswählen")]
    [Tooltip("Optional: Controller, mit dem gezeigt wird. Leer = automatisch der rechte Controller-Anker.")]
    [SerializeField] private Transform pointerTransform;
    [SerializeField] private OVRInput.Controller pointerController = OVRInput.Controller.RTouch;
    [SerializeField] private OVRInput.Button selectButton = OVRInput.Button.PrimaryIndexTrigger;

    [Header("Menü-Position (am linken Controller)")]
    [Tooltip("Optional: Anker, dem das Menü folgt. Leer = automatisch der linke Controller-Anker.")]
    [SerializeField] private Transform menuAnchor;
    [Tooltip("Versatz gegenüber der linken Hand (in Handkoordinaten). Das Panel dreht sich " +
             "immer automatisch zur Kamera, damit es lesbar bleibt.")]
    [SerializeField] private Vector3 menuFollowOffset = new(0f, 0.08f, 0.02f);
    [Tooltip("Taste am linken Controller zum Ein-/Ausblenden des Menüs (B/Y-Taste = 'Two').")]
    [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.Two;
    [SerializeField] private OVRInput.Controller toggleController = OVRInput.Controller.LTouch;
    [SerializeField] private bool startVisible = true;

    [Header("Aussehen")]
    [SerializeField] private Color panelColor = new(0.09f, 0.10f, 0.13f, 0.92f);
    [SerializeField] private Color buttonColor = new(0.20f, 0.22f, 0.28f, 1f);
    [SerializeField] private Color buttonHoverColor = new(0.20f, 0.55f, 0.95f, 1f);
    [SerializeField] private Color laserColor = new(0.20f, 0.60f, 1f, 1f);

    // ----- Statische Schnittstelle für andere Werkzeuge (z. B. das Messband) -----
    /// <summary>True, während der Zeiger-Controller über einem Menü-Button schwebt.</summary>
    public static bool IsHoveringMenu { get; private set; }
    /// <summary>Der Controller, mit dem auf das Menü gezeigt wird.</summary>
    public static OVRInput.Controller ActivePointerController { get; private set; }

    private class MenuButton
    {
        public RectTransform Rect;
        public Image Background;
        public Action Action;
    }

    private readonly List<MenuButton> buttons = new();
    private readonly List<GameObject> spawnedLevelers = new();
    private readonly List<Transform> selectedObjects = new();
    private readonly Color[] selectionColors =
    {
        Color.red,
        Color.green,
        Color.blue,
        Color.yellow,
        Color.magenta
    };
    private int colorCycleIndex;

    private RectTransform menuRoot;
    private LineRenderer laser;
    private RectTransform cursor;
    private MenuButton hovered;
    private bool visible;

    private void Awake()
    {
        ActivePointerController = pointerController;
        ResolveAnchors();
        BuildMenu();
        BuildLaser();
        SetVisible(startVisible);
        PositionMenu();
    }

    private void ResolveAnchors()
    {
        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (menuAnchor == null)
            menuAnchor = rig != null ? rig.leftControllerAnchor : FindByName("LeftControllerAnchor");
        if (pointerTransform == null)
            pointerTransform = rig != null ? rig.rightControllerAnchor : FindByName("RightControllerAnchor");

        if (menuAnchor == null || pointerTransform == null)
            Debug.LogError("[ToolMenu] Menü-Anker oder Zeiger-Controller konnte nicht gefunden werden. " +
                           "Bitte 'menuAnchor' und 'pointerTransform' im Inspector setzen.");
    }

    private static Transform FindByName(string n)
    {
        var go = GameObject.Find(n);
        return go != null ? go.transform : null;
    }

    // ---------------------------------------------------------------- Update ----

    private void Update()
    {
        ActivePointerController = pointerController;

        if (OVRInput.GetDown(toggleButton, toggleController))
            SetVisible(!visible);

        if (!visible || menuRoot == null || pointerTransform == null)
        {
            IsHoveringMenu = false;
            return;
        }

        UpdatePointer();

        if (hovered != null && OVRInput.GetDown(selectButton, pointerController))
            hovered.Action?.Invoke();
    }

    // Nach allen Bewegungen: Panel an die Hand setzen und zur Kamera drehen.
    private void LateUpdate()
    {
        if (visible) PositionMenu();
    }

    private void PositionMenu()
    {
        if (menuRoot == null || menuAnchor == null) return;

        Vector3 pos = menuAnchor.position + menuAnchor.rotation * menuFollowOffset;
        menuRoot.position = pos;

        var cam = Camera.main;
        if (cam != null)
            menuRoot.rotation = Quaternion.LookRotation(pos - cam.transform.position, Vector3.up);
    }

    private void UpdatePointer()
    {
        Vector3 origin = pointerTransform.position;
        Vector3 dir = pointerTransform.forward;

        hovered = null;
        Vector3 hitPoint = origin + dir * 2f;

        // Ray gegen die Menü-Ebene schneiden (beidseitig, unabhängig von der Blickrichtung).
        Vector3 normal = menuRoot.forward;
        float denom = Vector3.Dot(normal, dir);
        if (Mathf.Abs(denom) > 1e-6f)
        {
            float t = Vector3.Dot(menuRoot.position - origin, normal) / denom;
            if (t > 0f && t < 5f)
            {
                hitPoint = origin + dir * t;
                foreach (var b in buttons)
                {
                    Vector3 local = b.Rect.InverseTransformPoint(hitPoint);
                    if (b.Rect.rect.Contains(local))
                    {
                        hovered = b;
                        break;
                    }
                }
            }
        }

        // Button-Highlight
        foreach (var b in buttons)
            b.Background.color = (b == hovered) ? buttonHoverColor : buttonColor;

        // Laser & Zeiger aktualisieren
        laser.enabled = true;
        laser.SetPosition(0, origin);
        laser.SetPosition(1, hitPoint);

        cursor.gameObject.SetActive(hovered != null);
        if (hovered != null)
            cursor.position = hitPoint;

        IsHoveringMenu = hovered != null;
    }

    private void SetVisible(bool value)
    {
        visible = value;
        if (menuRoot != null) menuRoot.gameObject.SetActive(value);
        if (laser != null) laser.enabled = value;
        if (!value) IsHoveringMenu = false;
    }

    // ------------------------------------------------------------- Aktionen ----

    private void SpawnLeveler()
    {
        if (levelerPrefab == null)
        {
            Debug.LogError("[ToolMenu] 'levelerPrefab' ist nicht zugewiesen. Bitte im Inspector das " +
                           "LevelerFeature-Prefab zuweisen.");
            return;
        }

        Vector3 pos = pointerTransform.position + pointerTransform.forward * spawnDistance;
        var instance = Instantiate(levelerPrefab, pos, Quaternion.identity);

        // Zum Nutzer ausrichten.
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.transform.position - pos;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-4f)
                instance.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        spawnedLevelers.Add(instance);
    }

    private void DeleteLastLeveler()
    {
        for (int i = spawnedLevelers.Count - 1; i >= 0; i--)
        {
            if (spawnedLevelers[i] != null)
            {
                Destroy(spawnedLevelers[i]);
                spawnedLevelers.RemoveAt(i);
                return;
            }
            spawnedLevelers.RemoveAt(i); // aufräumen, falls schon zerstört
        }
    }

    private void ClearLines()
    {
        var tape = FindAnyObjectByType<MeasurinTapeFeature>();
        if (tape != null) tape.ClearAllLines();
        selectedObjects.Clear();
    }

    private void SelectSingleObject()
    {
        selectedObjects.Clear();
        var all = GetSelectableObjects();
        if (all.Count > 0)
            selectedObjects.Add(all[all.Count - 1]);
    }

    private void SelectMultipleObjects()
    {
        selectedObjects.Clear();
        selectedObjects.AddRange(GetSelectableObjects());
    }

    private void ChangeSelectedObjectColor()
    {
        if (selectedObjects.Count == 0)
        {
            SelectMultipleObjects();
            if (selectedObjects.Count == 0)
            {
                Debug.LogWarning("[ToolMenu] Keine Objekte zum Ändern der Farbe ausgewählt.");
                return;
            }
        }

        var color = selectionColors[colorCycleIndex % selectionColors.Length];
        colorCycleIndex++;

        foreach (var t in selectedObjects)
            ApplyColorToObject(t, color);
    }

    private void AdjustSelectedObjectOrientation()
    {
        if (selectedObjects.Count == 0)
        {
            SelectMultipleObjects();
            if (selectedObjects.Count == 0)
            {
                Debug.LogWarning("[ToolMenu] Keine Objekte zur Orientierung ausgewählt.");
                return;
            }
        }

        foreach (var t in selectedObjects)
            if (t != null)
                t.Rotate(0f, 15f, 0f, Space.World);
    }

    private List<Transform> GetSelectableObjects()
    {
        var objects = new List<Transform>();

        foreach (var leveler in FindObjectsByType<LevelerFeature>(FindObjectsSortMode.None))
        {
            if (leveler != null && leveler.transform != null)
                objects.Add(leveler.transform);
        }

        var tape = FindAnyObjectByType<MeasurinTapeFeature>();
        if (tape != null)
        {
            foreach (var line in tape.Lines)
            {
                if (line != null && line.TapeLine != null)
                    objects.Add(line.TapeLine.transform);
            }
        }

        return objects;
    }

    private static void ApplyColorToObject(Transform root, Color color)
    {
        if (root == null) return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
                mats[i].color = color;
            renderer.materials = mats;
        }

        foreach (var lineRenderer in root.GetComponentsInChildren<LineRenderer>(true))
            lineRenderer.material.color = color;

        var text = root.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.color = color;
    }

    private void ResetAll()
    {
        foreach (var l in spawnedLevelers)
            if (l != null) Destroy(l);
        spawnedLevelers.Clear();
        selectedObjects.Clear();
        ClearLines();
    }

    // --------------------------------------------------------- Menü-Aufbau ----

    private void BuildMenu()
    {
        // Wurzel-Canvas (World Space). Bewusst NICHT an den Controller geparentet –
        // Position & Ausrichtung werden in LateUpdate gesetzt, damit das Panel der
        // Hand folgt, sich aber immer zur Kamera dreht (sonst dreht es kantig weg
        // und verschwindet).
        var canvasGo = new GameObject("ToolMenuCanvas", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        menuRoot = canvasGo.GetComponent<RectTransform>();
        menuRoot.sizeDelta = new Vector2(260f, 620f);
        menuRoot.localScale = Vector3.one * 0.0006f; // ~15,6 cm x 37,2 cm

        // Hintergrundpanel
        AddImage(menuRoot, "Panel", panelColor, Vector2.zero, menuRoot.sizeDelta);

        // Titel
        var title = AddText(menuRoot, "Title", "Werkzeuge", 26f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 270f);
        title.rectTransform.sizeDelta = new Vector2(240f, 40f);

        // Buttons
        string[] labels =
        {
            "Leveler +",
            "Leveler -",
            "Auswahl",
            "Einzel wählen",
            "Mehrere wählen",
            "Farbe",
            "Orientierung",
            "Linien löschen",
            "Reset"
        };
        Action[] actions =
        {
            SpawnLeveler,
            DeleteLastLeveler,
            SelectMultipleObjects,
            SelectSingleObject,
            SelectMultipleObjects,
            ChangeSelectedObjectColor,
            AdjustSelectedObjectOrientation,
            ClearLines,
            ResetAll
        };
        float startY = 220f;
        float step = 58f;
        for (int i = 0; i < labels.Length; i++)
            CreateButton(labels[i], new Vector2(0f, startY - i * step), actions[i]);

        // Zeiger-Punkt
        var cursorImg = AddImage(menuRoot, "Cursor", laserColor, Vector2.zero, new Vector2(14f, 14f));
        cursor = cursorImg.rectTransform;
        cursor.gameObject.SetActive(false);
    }

    private void CreateButton(string label, Vector2 anchoredPos, Action action)
    {
        var bg = AddImage(menuRoot, "Button_" + label, buttonColor, anchoredPos, new Vector2(230f, 56f));
        var text = AddText(bg.rectTransform, "Label", label, 22f);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        buttons.Add(new MenuButton
        {
            Rect = bg.rectTransform,
            Background = bg,
            Action = action
        });
    }

    private static Image AddImage(Transform parent, string name, Color color, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI AddText(Transform parent, string name, string content, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    private void BuildLaser()
    {
        var laserGo = new GameObject("ToolMenuLaser", typeof(LineRenderer));
        laser = laserGo.GetComponent<LineRenderer>();
        laser.positionCount = 2;
        laser.startWidth = 0.004f;
        laser.endWidth = 0.004f;
        laser.useWorldSpace = true;

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", laserColor);
        mat.color = laserColor;
        laser.material = mat;
        laser.startColor = laser.endColor = laserColor;
        laser.enabled = false;
    }
}
