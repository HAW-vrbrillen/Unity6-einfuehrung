using System.Collections.Generic;
using UnityEngine;

public class OvenFeature : MonoBehaviour
{
    private enum Control
    {
        Door,
        Temperature,
        Timer
    }

    private const float OvenWidth = 0.9f;
    private const float OvenHeight = 1.25f;
    private const float OvenDepth = 0.65f;
    private const float InteractionDistance = 3f;
    private const int MaxSetting = 10;

    private static readonly OVRInput.Controller[] Controllers =
    {
        OVRInput.Controller.LTouch,
        OVRInput.Controller.RTouch
    };

    private readonly Dictionary<Collider, Control> interactableParts = new();
    private Transform[] controllerAnchors;
    private LineRenderer[] pointerLines;
    private Transform doorHinge;
    private Transform temperatureKnob;
    private Transform timerKnob;
    private bool doorOpen;
    private int temperatureSetting;
    private int timerSetting;

    public static void CreateInScene()
    {
        if (FindAnyObjectByType<OvenFeature>() != null) return;

        var oven = new GameObject("Waermebehandlungsofen");
        oven.AddComponent<OvenFeature>();
    }

    private void Start()
    {
        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig == null || rig.centerEyeAnchor == null)
        {
            Debug.LogError("[OvenFeature] Kein OVRCameraRig mit centerEyeAnchor gefunden. Der Ofen wurde nicht erstellt.");
            Destroy(gameObject);
            return;
        }

        controllerAnchors = new[] { rig.leftControllerAnchor, rig.rightControllerAnchor };
        pointerLines = new LineRenderer[controllerAnchors.Length];

        Vector3 forward = Vector3.ProjectOnPlane(rig.centerEyeAnchor.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

        transform.position = rig.centerEyeAnchor.position + forward * 1.8f - Vector3.up * 0.93f;
        transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        BuildOven();
    }

    private void Update()
    {
        if (controllerAnchors == null) return;

        for (int i = 0; i < controllerAnchors.Length; i++)
        {
            var anchor = controllerAnchors[i];
            if (anchor == null) continue;

            bool menuHasPointer = ToolMenu.IsHoveringMenu
                                  && Controllers[i] == ToolMenu.ActivePointerController;
            var target = UpdatePointer(i, anchor, menuHasPointer);

            if (!menuHasPointer && target.HasValue
                                 && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, Controllers[i]))
            {
                Interact(target.Value);
            }
        }
    }

    private void BuildOven()
    {
        var shell = CreatePart("Ofengehaeuse", PrimitiveType.Cube, transform,
            Vector3.zero, Vector3.zero, new Vector3(OvenWidth, OvenHeight, OvenDepth),
            new Color(0.19f, 0.22f, 0.25f));
        shell.GetComponent<Collider>().enabled = false;

        CreatePart("Frontblende", PrimitiveType.Cube, transform,
            new Vector3(0f, -0.02f, OvenDepth * 0.5f + 0.008f), Vector3.zero,
            new Vector3(OvenWidth * 0.88f, OvenHeight * 0.82f, 0.025f),
            new Color(0.29f, 0.32f, 0.34f));

        CreatePart("Display", PrimitiveType.Cube, transform,
            new Vector3(0.25f, 0.36f, OvenDepth * 0.5f + 0.03f), Vector3.zero,
            new Vector3(0.25f, 0.09f, 0.025f), new Color(0.1f, 0.32f, 0.34f));

        CreateKnob("Temperaturregler", new Vector3(0.23f, 0.19f, OvenDepth * 0.5f + 0.055f), false);
        CreateKnob("Zeitregler", new Vector3(0.36f, 0.19f, OvenDepth * 0.5f + 0.055f), true);

        doorHinge = new GameObject("Tuer-Scharnier").transform;
        doorHinge.SetParent(transform, false);
        doorHinge.localPosition = new Vector3(-OvenWidth * 0.5f + 0.045f, -0.02f, OvenDepth * 0.5f + 0.05f);

        var door = CreatePart("Ofentuer", PrimitiveType.Cube, doorHinge,
            new Vector3(0.26f, 0f, 0f), Vector3.zero,
            new Vector3(0.52f, OvenHeight * 0.82f, 0.075f),
            new Color(0.23f, 0.26f, 0.29f));
        interactableParts.Add(door.GetComponent<Collider>(), Control.Door);

        CreatePart("Sichtfenster", PrimitiveType.Cube, door.transform,
            new Vector3(0f, 0.12f, 0.05f), Vector3.zero,
            new Vector3(0.34f, 0.4f, 0.008f), new Color(0.08f, 0.15f, 0.19f))
            .GetComponent<Collider>().enabled = false;

        CreatePart("FensterrahmenOben", PrimitiveType.Cube, door.transform,
            new Vector3(0f, 0.345f, 0.045f), Vector3.zero,
            new Vector3(0.38f, 0.012f, 0.008f), new Color(0.62f, 0.66f, 0.68f))
            .GetComponent<Collider>().enabled = false;
        CreatePart("FensterrahmenUnten", PrimitiveType.Cube, door.transform,
            new Vector3(0f, -0.105f, 0.045f), Vector3.zero,
            new Vector3(0.38f, 0.012f, 0.008f), new Color(0.62f, 0.66f, 0.68f))
            .GetComponent<Collider>().enabled = false;
        CreatePart("FensterrahmenLinks", PrimitiveType.Cube, door.transform,
            new Vector3(-0.185f, 0.12f, 0.045f), Vector3.zero,
            new Vector3(0.012f, 0.44f, 0.008f), new Color(0.62f, 0.66f, 0.68f))
            .GetComponent<Collider>().enabled = false;
        CreatePart("FensterrahmenRechts", PrimitiveType.Cube, door.transform,
            new Vector3(0.185f, 0.12f, 0.045f), Vector3.zero,
            new Vector3(0.012f, 0.44f, 0.008f), new Color(0.62f, 0.66f, 0.68f))
            .GetComponent<Collider>().enabled = false;

        CreatePart("Tuergriff", PrimitiveType.Cube, door.transform,
            new Vector3(0.18f, 0.23f, 0.06f), Vector3.zero,
            new Vector3(0.2f, 0.035f, 0.045f), new Color(0.72f, 0.74f, 0.75f))
            .GetComponent<Collider>().enabled = false;
    }

    private void CreateKnob(string name, Vector3 position, bool isTimer)
    {
        var knob = new GameObject(name);
        knob.transform.SetParent(transform, false);
        knob.transform.localPosition = position;

        var knobCollider = CreatePart("Drehknopf", PrimitiveType.Cylinder, knob.transform,
            Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.11f, 0.025f, 0.11f),
            isTimer ? new Color(0.76f, 0.48f, 0.17f) : new Color(0.7f, 0.72f, 0.74f))
            .GetComponent<Collider>();
        interactableParts.Add(knobCollider,
            isTimer ? Control.Timer : Control.Temperature);
        if (isTimer) timerKnob = knob.transform;
        else temperatureKnob = knob.transform;

        CreatePart("Markierung", PrimitiveType.Cube, knob.transform,
            new Vector3(0f, 0.04f, 0.034f), Vector3.zero,
            new Vector3(0.012f, 0.025f, 0.012f), new Color(0.08f, 0.09f, 0.1f))
            .GetComponent<Collider>().enabled = false;

        for (int i = 0; i < MaxSetting; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            var tick = CreatePart("Skalenstrich", PrimitiveType.Cube, transform,
                position + new Vector3(Mathf.Sin(angle) * 0.075f, Mathf.Cos(angle) * 0.075f, 0.012f),
                Vector3.zero, new Vector3(0.009f, 0.018f, 0.008f),
                new Color(0.85f, 0.86f, 0.86f));
            tick.GetComponent<Collider>().enabled = false;
        }
    }

    private Control? UpdatePointer(int index, Transform anchor, bool hidden)
    {
        var pointer = pointerLines[index];
        if (pointer == null)
        {
            var lineObject = new GameObject("Ofen-Zeiger");
            lineObject.transform.SetParent(anchor, false);
            pointer = lineObject.AddComponent<LineRenderer>();
            pointer.useWorldSpace = true;
            pointer.positionCount = 2;
            pointer.startWidth = 0.004f;
            pointer.endWidth = 0.001f;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[OvenFeature] Kein passender Shader fuer den Controller-Zeiger gefunden.");
                Destroy(lineObject);
                return null;
            }
            pointer.material = new Material(shader);
            pointer.startColor = new Color(0.25f, 0.9f, 1f);
            pointer.endColor = new Color(0.25f, 0.9f, 1f, 0.2f);
            pointerLines[index] = pointer;
        }

        if (hidden)
        {
            pointer.enabled = false;
            return null;
        }

        var ray = new Ray(anchor.position, anchor.forward);
        var hits = Physics.RaycastAll(ray, InteractionDistance);
        Control? target = null;
        float closestDistance = float.MaxValue;
        Vector3 endPoint = ray.origin + ray.direction * InteractionDistance;

        foreach (var hit in hits)
        {
            if (!interactableParts.TryGetValue(hit.collider, out var control)
                || hit.distance >= closestDistance)
                continue;

            target = control;
            closestDistance = hit.distance;
            endPoint = hit.point;
        }

        pointer.enabled = target != null;
        if (pointer.enabled)
        {
            pointer.SetPosition(0, anchor.position);
            pointer.SetPosition(1, endPoint);
        }
        return target;
    }

    private void Interact(Control control)
    {
        switch (control)
        {
            case Control.Door:
                doorOpen = !doorOpen;
                doorHinge.localRotation = Quaternion.Euler(0f, doorOpen ? -105f : 0f, 0f);
                return;
            case Control.Temperature:
                temperatureSetting = (temperatureSetting + 1) % (MaxSetting + 1);
                temperatureKnob.localRotation = Quaternion.Euler(0f, 0f, temperatureSetting * -30f);
                return;
            case Control.Timer:
                timerSetting = (timerSetting + 1) % (MaxSetting + 1);
                timerKnob.localRotation = Quaternion.Euler(0f, 0f, timerSetting * -30f);
                return;
        }
    }

    private GameObject CreatePart(string name, PrimitiveType type, Transform parent,
        Vector3 localPosition, Vector3 localRotation, Vector3 localScale, Color color)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.Euler(localRotation);
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().material.color = color;
        return part;
    }
}
