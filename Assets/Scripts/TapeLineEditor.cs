using UnityEngine;

/// <summary>
/// Erlaubt es, bereits gesetzte Messlinien mit dem Controller zu bearbeiten:
///   • Auf eine Linie zeigen  → sie wird hervorgehoben.
///   • Grip halten            → die Linie folgt dem Controller (verschieben).
///   • A-Taste drücken        → nur DIESE eine Linie löschen.
///
/// Nutzt einen eigenen Strahl (kein Interaction SDK nötig). Einfach an ein
/// beliebiges GameObject in der Szene hängen – Controller wird automatisch gesucht.
/// </summary>
public class TapeLineEditor : MonoBehaviour
{
    [Tooltip("Optional: Controller zum Zeigen. Leer = automatisch der rechte Controller-Anker.")]
    [SerializeField] private Transform pointerTransform;
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
    [Tooltip("Taste zum Verschieben (gehalten). Standard: Grip / Hand-Trigger.")]
    [SerializeField] private OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger;
    [Tooltip("Taste zum Löschen dieser einen Linie. Standard: A-Taste.")]
    [SerializeField] private OVRInput.Button deleteButton = OVRInput.Button.One;
    [Tooltip("Wie nah der Strahl an einer Linie sein muss, um sie zu treffen (Meter).")]
    [SerializeField] private float maxTargetDistance = 0.08f;
    [SerializeField] private Color highlightColor = Color.cyan;

    private MeasurinTapeFeature tape;
    private MeasuringTape targeted;
    private MeasuringTape grabbed;
    private Vector3 lastPointerPos;

    private LineRenderer highlightedRenderer;
    private Color highlightedOriginal;

    private void Awake()
    {
        if (pointerTransform == null)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null) pointerTransform = rig.rightControllerAnchor;
        }
    }

    private void Update()
    {
        if (tape == null) tape = FindAnyObjectByType<MeasurinTapeFeature>();
        if (tape == null || pointerTransform == null) return;

        // Läuft gerade eine Verschiebung? -> nur das behandeln.
        if (grabbed != null)
        {
            HandleGrabbing();
            return;
        }

        // Menü hat Vorrang: nicht gleichzeitig Linien anvisieren.
        if (ToolMenu.IsHoveringMenu && controller == ToolMenu.ActivePointerController)
        {
            ClearTarget();
            return;
        }

        UpdateTargeting();

        if (targeted != null)
        {
            if (OVRInput.GetDown(grabButton, controller))
                StartGrab();
            else if (OVRInput.GetDown(deleteButton, controller))
                DeleteTargeted();
        }
    }

    // ------------------------------------------------------------- Anvisieren --

    private void UpdateTargeting()
    {
        var ray = new Ray(pointerTransform.position, pointerTransform.forward);

        MeasuringTape best = null;
        float bestDist = maxTargetDistance;

        foreach (var t in tape.Lines)
        {
            var lr = GetRenderer(t);
            if (lr == null) continue;

            float d = RayToSegment(ray, lr.GetPosition(0), lr.GetPosition(1));
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }

        SetTarget(best);
    }

    private void SetTarget(MeasuringTape t)
    {
        if (t == targeted) return;

        ClearTarget();
        targeted = t;

        var lr = GetRenderer(t);
        if (lr != null)
        {
            highlightedRenderer = lr;
            highlightedOriginal = lr.material.color;
            lr.material.color = highlightColor;
        }
    }

    private void ClearTarget()
    {
        if (highlightedRenderer != null)
        {
            highlightedRenderer.material.color = highlightedOriginal;
            highlightedRenderer = null;
        }
        targeted = null;
    }

    // ------------------------------------------------------------- Verschieben -

    private void StartGrab()
    {
        grabbed = targeted;
        lastPointerPos = pointerTransform.position;
    }

    private void HandleGrabbing()
    {
        // Grip losgelassen oder Linie weg -> Verschieben beenden.
        if (grabbed == null || !OVRInput.Get(grabButton, controller))
        {
            grabbed = null;
            return;
        }

        var lr = GetRenderer(grabbed);
        if (lr == null)
        {
            grabbed = null;
            return;
        }

        Vector3 now = pointerTransform.position;
        Vector3 delta = now - lastPointerPos;
        lastPointerPos = now;

        // Beide Endpunkte und die Anzeige um dieselbe Strecke verschieben.
        lr.SetPosition(0, lr.GetPosition(0) + delta);
        lr.SetPosition(1, lr.GetPosition(1) + delta);
        if (grabbed.TapeInfo != null)
            grabbed.TapeInfo.transform.position += delta;
    }

    // ------------------------------------------------------------- Löschen -----

    private void DeleteTargeted()
    {
        var t = targeted;
        ClearTarget();       // Farbe zurücksetzen, bevor die Linie zerstört wird
        tape.DeleteLine(t);
    }

    // ------------------------------------------------------------- Helfer ------

    private static LineRenderer GetRenderer(MeasuringTape t)
        => t != null && t.TapeLine != null ? t.TapeLine.GetComponent<LineRenderer>() : null;

    // Kleinster Abstand zwischen dem Strahl und der Strecke a–b (über Abtastung).
    private static float RayToSegment(Ray ray, Vector3 a, Vector3 b, int samples = 12)
    {
        float min = float.MaxValue;
        for (int i = 0; i <= samples; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)samples);
            float d = PointToRayDistance(ray, p);
            if (d < min) min = d;
        }
        return min;
    }

    private static float PointToRayDistance(Ray ray, Vector3 point)
    {
        Vector3 v = point - ray.origin;
        float t = Mathf.Max(0f, Vector3.Dot(v, ray.direction)); // ray.direction ist normiert
        Vector3 closest = ray.origin + ray.direction * t;
        return Vector3.Distance(point, closest);
    }
}
