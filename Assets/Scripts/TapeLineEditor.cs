using UnityEngine;

/// <summary>
/// Erlaubt es, bereits gesetzte Messlinien mit dem Controller zu bearbeiten:
///   • Auf eine Linie zeigen  → sie wird hervorgehoben.
///   • Grip halten            → die Linie folgt dem Controller (verschieben).
///   • A-Taste halten         → einen Kreis zeichnen oder dessen Radius ändern.
///   • A-Taste auf einer Linie → beim Loslassen diese Linie löschen.
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
    [Header("Kreise")]
    [SerializeField] private float initialCircleRadius = 0.1f;
    [SerializeField] private float minCircleRadius = 0.02f;
    [SerializeField] private float maxCircleRadius = 2f;
    [SerializeField] private float radiusChangePerMeter = 1f;
    [SerializeField] private float circleLineWidth = 0.005f;
    [SerializeField] private int circleSegments = 64;
    [SerializeField] private Color circleColor = Color.green;

    private MeasurinTapeFeature tape;
    private MeasuringTape targeted;
    private MeasuringTape pendingDelete;
    private MeasuringTape grabbed;
    private Vector3 lastPointerPos;
    private Vector3 circleStartPosition;
    private float circleStartY;
    private float circleStartRadius;
    private float currentCircleRadius;
    private bool drawingCircle;
    private int circleCount;
    private LineRenderer currentCircle;

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
            if (OVRInput.GetUp(deleteButton, controller))
            {
                drawingCircle = false;
                currentCircle = null;
                pendingDelete = null;
            }
            return;
        }

        UpdateTargeting();
        HandleCircleInput();

        if (targeted != null)
        {
            if (OVRInput.GetDown(grabButton, controller))
                StartGrab();
        }
    }

    private void HandleCircleInput()
    {
        if (OVRInput.GetDown(deleteButton, controller))
        {
            if (targeted != null)
            {
                pendingDelete = targeted;
                circleStartPosition = pointerTransform.position;
                circleStartY = pointerTransform.position.y;
                circleStartRadius = GetInitialCircleRadius();
            }
            else
            {
                StartCircle(pointerTransform.position);
            }
        }

        if (OVRInput.Get(deleteButton, controller))
        {
            float verticalMovement = circleStartY - pointerTransform.position.y;
            if (pendingDelete != null && Mathf.Abs(verticalMovement) >= 0.02f)
            {
                pendingDelete = null;
                ClearTarget();
                StartCircle(circleStartPosition);
            }

            if (drawingCircle)
            {
                currentCircleRadius = Mathf.Clamp(
                    circleStartRadius + verticalMovement * radiusChangePerMeter,
                    minCircleRadius,
                    maxCircleRadius);
                UpdateCirclePoints();
            }
        }

        if (OVRInput.GetUp(deleteButton, controller))
        {
            if (drawingCircle)
            {
                drawingCircle = false;
                currentCircle = null;
            }
            else if (pendingDelete != null)
            {
                var target = pendingDelete;
                pendingDelete = null;
                if (target == targeted) DeleteTargeted();
                else tape.DeleteLine(target);
            }
        }
    }

    private void StartCircle(Vector3 center)
    {
        circleStartPosition = center;
        circleStartY = center.y;
        circleStartRadius = GetInitialCircleRadius();
        currentCircleRadius = circleStartRadius;
        currentCircle = new GameObject($"Circle_{circleCount++}", typeof(LineRenderer))
            .GetComponent<LineRenderer>();
        currentCircle.useWorldSpace = true;
        currentCircle.loop = true;
        currentCircle.positionCount = Mathf.Max(3, circleSegments);
        currentCircle.startWidth = circleLineWidth;
        currentCircle.endWidth = circleLineWidth;
        currentCircle.startColor = circleColor;
        currentCircle.endColor = circleColor;

        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            currentCircle.material = new Material(shader);
            currentCircle.material.color = circleColor;
        }

        drawingCircle = true;
        UpdateCirclePoints();
    }

    private float GetInitialCircleRadius()
    {
        float minRadius = Mathf.Max(0.001f, minCircleRadius);
        float maxRadius = Mathf.Max(minRadius, maxCircleRadius);
        return Mathf.Clamp(initialCircleRadius, minRadius, maxRadius);
    }

    private void UpdateCirclePoints()
    {
        if (currentCircle == null) return;

        int segments = currentCircle.positionCount;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 offset = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            currentCircle.SetPosition(i, circleStartPosition + offset * currentCircleRadius);
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
