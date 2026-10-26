using System.Collections.Generic;
using System.Linq;
using LearnXR.Core;
using LearnXR.Core.Utilities;
using TMPro;
using UnityEngine;

public class MeasurinTapeFeature : Singleton<MeasurinTapeFeature>
{
    [Range(0.005f, 0.05f)]
    [SerializeField] private float tapeWidth = 0.01f;
    [SerializeField] private OVRInput.Button tapeActionButton;
    [SerializeField] private Material tapeMaterial;
    [SerializeField] private GameObject measurementInfoPrefab;
    [SerializeField] private Vector3 measurementInfoControllerOffset = new(0, 0.045f, 0);

    [Tooltip("Wie weit die Maßzahl seitlich (senkrecht) neben der Linie liegt, damit sie besser lesbar ist (Meter).")]
    [SerializeField] private float measurementInfoLineOffset = 0.06f;

    [SerializeField]
    private string measurementInfoFormat =
        "<mark=#0000005A padding=\"20, 20, 10, 10\"><color=yellow>{0}</color></mark>";

    [SerializeField] private Transform leftControllerTapeArea;
    [SerializeField] private Transform rightControllerTapeArea;

    private List<MeasuringTape> savedTapeLines = new();
    private TextMeshPro lastMeasurementInfo;
    private LineRenderer lastTapeLineRenderer;
    private OVRInput.Controller? currentController;

    private void Awake() => ResolveTapeAreas();

    // Sorgt dafür, dass die Linien am echten Controller starten, auch wenn die
    // TapeArea-Referenzen im Inspector fehlerhaft sind (leer oder beide identisch).
    private void ResolveTapeAreas()
    {
        bool misconfigured = leftControllerTapeArea == null
                             || rightControllerTapeArea == null
                             || leftControllerTapeArea == rightControllerTapeArea;
        if (!misconfigured) return;

        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig == null)
        {
            Debug.LogError("[MeasurinTapeFeature] TapeArea-Referenzen sind fehlerhaft (leer oder beide " +
                           "identisch) und es wurde kein OVRCameraRig gefunden. Linienzeichnen funktioniert nicht.");
            return;
        }

        leftControllerTapeArea = rig.leftControllerAnchor;
        rightControllerTapeArea = rig.rightControllerAnchor;
        Debug.LogWarning("[MeasurinTapeFeature] TapeArea-Referenzen waren fehlerhaft (leer oder identisch). " +
                         "Automatisch auf die OVR-Controller-Anker gesetzt, damit Linien am Controller starten.");
    }

    private void Update()
    {
        HandleControllerActions(OVRInput.Controller.LTouch, leftControllerTapeArea);
        HandleControllerActions(OVRInput.Controller.RTouch, rightControllerTapeArea);
    }

    private void HandleControllerActions(OVRInput.Controller controller, Transform tapeArea)
    {
        if (tapeArea == null) return;

        // Wenn dieser Controller gerade auf das Werkzeug-Menü zeigt, nicht zeichnen.
        if (ToolMenu.IsHoveringMenu && controller == ToolMenu.ActivePointerController) return;

        if (currentController != controller && currentController != null) return;

        if (OVRInput.GetDown(tapeActionButton, controller))
        {
            currentController = controller;
            HandleDownAction(tapeArea);
        }

        if (OVRInput.Get(tapeActionButton, controller))
        {
            HandleHoldAction(tapeArea);
        }

        if (OVRInput.GetUp(tapeActionButton, controller))
        {
            currentController = null;
            HandleUpAction(tapeArea);
        }
    }

    private void HandleDownAction(Transform tapeArea)
    {
        CreateNewTapeLine(tapeArea.position);
        AttachAndDetachMeasurementInfo(tapeArea);
    }

    private void HandleHoldAction(Transform tapeArea)
    {
        if (lastTapeLineRenderer == null)
            return;

        lastTapeLineRenderer.SetPosition(1, tapeArea.position);
        CalculateMeasurements();
    }

    private void HandleUpAction(Transform tapeArea)
    {
        AttachAndDetachMeasurementInfo(tapeArea, false);
    }

    private void CreateNewTapeLine(Vector3 initialPosition)
    {
        var newTapeLine = new GameObject($"TapeLine_{savedTapeLines.Count}", typeof(LineRenderer));

        lastTapeLineRenderer = newTapeLine.GetComponent<LineRenderer>();
        lastTapeLineRenderer.positionCount = 2;
        lastTapeLineRenderer.startWidth = tapeWidth;
        lastTapeLineRenderer.endWidth = tapeWidth;
        lastTapeLineRenderer.material = tapeMaterial;
        lastTapeLineRenderer.SetPosition(0, initialPosition);
        lastTapeLineRenderer.SetPosition(1, initialPosition);

        if (measurementInfoPrefab == null)
        {
            Debug.LogError("[MeasurinTapeFeature] 'measurementInfoPrefab' ist nicht zugewiesen. " +
                           "Die Linie wird gezeichnet, aber ohne Messwert-Anzeige. " +
                           "Bitte im Inspector das MeasureInfo-Prefab zuweisen.");
            lastMeasurementInfo = null;
            savedTapeLines.Add(new MeasuringTape { TapeLine = newTapeLine, TapeInfo = null });
            return;
        }

        lastMeasurementInfo = Instantiate(measurementInfoPrefab, Vector3.zero, Quaternion.identity)
            .GetComponent<TextMeshPro>();

        // Anzeige immer zur Kamera ausrichten. Das paket-eigene BillboardAlignment
        // spiegelte den Text aus manchen Blickwinkeln -> deaktivieren und stattdessen
        // die robuste, kamera-parallele Ausrichtung (FaceCamera) verwenden.
        var packageBillboard = lastMeasurementInfo.GetComponent<BillboardAlignment>();
        if (packageBillboard != null) packageBillboard.enabled = false;

        var faceCamera = lastMeasurementInfo.GetComponent<FaceCamera>();
        if (faceCamera == null)
            faceCamera = lastMeasurementInfo.gameObject.AddComponent<FaceCamera>();
        if (Camera.main != null)
            faceCamera.SetCamera(Camera.main.transform);

        lastMeasurementInfo.gameObject.SetActive(false);

        savedTapeLines.Add(new MeasuringTape
        {
            TapeLine = newTapeLine,
            TapeInfo = lastMeasurementInfo
        });
    }

    private void AttachAndDetachMeasurementInfo(Transform tapeArea, bool attachToController = true)
    {
        if (lastMeasurementInfo == null)
            return;

        if (attachToController)
        {
            lastMeasurementInfo.gameObject.SetActive(true);
            lastMeasurementInfo.transform.SetParent(tapeArea, false);
            lastMeasurementInfo.transform.localPosition = measurementInfoControllerOffset;
        }
        else
        {
            lastMeasurementInfo.transform.SetParent(lastTapeLineRenderer.transform, true);

            // Mittelpunkt der Linie, dann seitlich (senkrecht) versetzt, damit die
            // Anzeige nicht auf der Linie liegt und besser lesbar ist.
            Vector3 p0 = lastTapeLineRenderer.GetPosition(0);
            Vector3 p1 = lastTapeLineRenderer.GetPosition(1);
            Vector3 lineMidPoint = (p0 + p1) / 2.0f;
            lastMeasurementInfo.transform.position =
                lineMidPoint + PerpendicularOffset(p0, p1) * measurementInfoLineOffset;
        }
    }

    private void CalculateMeasurements()
    {
        if (lastMeasurementInfo == null || savedTapeLines.Count == 0)
            return;

        var distance = Vector3.Distance(lastTapeLineRenderer.GetPosition(0),
            lastTapeLineRenderer.GetPosition(1));

        var lastLine = savedTapeLines.Last();
        if (lastLine.TapeInfo == null) return;

        // Nur metrisch: unter 1 m in Zentimetern, ab 1 m in Metern.
        string label = distance >= 1.0f
            ? $"{distance:F2} m"
            : $"{MeasuringTape.MetersToCentimeters(distance):F1} cm";

        lastLine.TapeInfo.text = string.Format(measurementInfoFormat, label);
    }

    // Versatz senkrecht zur Linie, möglichst nach oben – damit die Anzeige daneben liegt.
    private static Vector3 PerpendicularOffset(Vector3 p0, Vector3 p1)
    {
        Vector3 dir = (p1 - p0).normalized;
        if (dir.sqrMagnitude < 1e-6f) return Vector3.up;

        // Up-Anteil, der senkrecht auf der Linie steht:
        Vector3 perp = Vector3.up - Vector3.Dot(Vector3.up, dir) * dir;
        if (perp.sqrMagnitude < 1e-4f) // Linie ist senkrecht -> Ausweichrichtung
            perp = Vector3.Cross(dir, Vector3.forward);
        return perp.normalized;
    }

    /// <summary>Alle aktuell gezeichneten Messlinien (nur lesen).</summary>
    public IReadOnlyList<MeasuringTape> Lines => savedTapeLines;

    private void UpdateLastLineReferences()
    {
        var lastTape = savedTapeLines.LastOrDefault();
        if (lastTape == null)
        {
            ResetLastLineState();
            return;
        }

        lastMeasurementInfo = lastTape.TapeInfo;
        lastTapeLineRenderer = lastTape.TapeLine != null
            ? lastTape.TapeLine.GetComponent<LineRenderer>()
            : null;
    }

    private void ResetLastLineState()
    {
        lastTapeLineRenderer = null;
        lastMeasurementInfo = null;
        currentController = null;
    }

    /// <summary>Entfernt EINE einzelne Messlinie samt Anzeige.</summary>
    public void DeleteLine(MeasuringTape tape)
    {
        if (tape == null) return;

        bool wasLastTape = ReferenceEquals(savedTapeLines.LastOrDefault(), tape);
        bool wasLastInfo = ReferenceEquals(lastMeasurementInfo, tape.TapeInfo);
        bool wasLastRenderer = ReferenceEquals(lastTapeLineRenderer, tape.TapeLine != null ? tape.TapeLine.GetComponent<LineRenderer>() : null);

        if (tape.TapeInfo != null) Destroy(tape.TapeInfo.gameObject);
        if (tape.TapeLine != null) Destroy(tape.TapeLine);
        savedTapeLines.Remove(tape);

        if (savedTapeLines.Count == 0)
        {
            ResetLastLineState();
            return;
        }

        if (wasLastTape || wasLastInfo || wasLastRenderer)
            UpdateLastLineReferences();
    }

    /// <summary>Anzahl der aktuell gezeichneten Messlinien.</summary>
    public int LineCount => savedTapeLines.Count;

    /// <summary>Entfernt alle gezeichneten Messlinien und deren Anzeigen.</summary>
    public void ClearAllLines()
    {
        foreach (var tape in savedTapeLines)
        {
            if (tape.TapeInfo != null) Destroy(tape.TapeInfo.gameObject);
            if (tape.TapeLine != null) Destroy(tape.TapeLine);
        }

        savedTapeLines.Clear();
        ResetLastLineState();
    }
}