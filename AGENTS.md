# Anleitung für KI-Agenten (Codex, Copilot, Claude)

Dieses Repository wird in einer Einführung für Lehrende genutzt: Teilnehmende beschreiben
in normaler Sprache eine Änderung, du setzt sie im Repository um, die Teilnehmenden holen
den Stand in Unity und testen ihn auf der Meta Quest 3. **Du kannst Unity nicht ausführen** –
prüfe deinen Code deshalb besonders sorgfältig, bevor du ihn pushst.

Antworte und schreibe Commit-Nachrichten **auf Deutsch**, in einfacher Sprache (die
Teilnehmenden sind keine Programmierer).

## Das Projekt

Mess-App für Mixed Reality auf der Meta Quest 3.

- Unity **6000.4.6f1** (Unity 6), Universal Render Pipeline 17, Meta XR SDK 201, Plattform Android
- Eingabe über Controller mit `OVRInput` (Meta XR SDK), Texte mit TextMeshPro
- Einzige Szene: `Assets/Scenes/SampleScene.unity`

| Datei | Funktion |
|---|---|
| `Assets/Scripts/ToolMenu.cs` | Menü am linken Controller, Laser am rechten; Menü, Buttons und Laser werden komplett per Code erzeugt (Farben, Größen, Abstände hier) |
| `Assets/Scripts/MeasurinTapeFeature.cs` | Maßband: Linien zwischen zwei Punkten ziehen, Länge anzeigen |
| `Assets/Scripts/TapeLineEditor.cs` | Gesetzte Messlinien verschieben (Grip) oder löschen (A-Taste) |
| `Assets/Scripts/MeasuringTape.cs` | Umrechnung Meter → Zentimeter/Zoll |
| `Assets/Scripts/LevelerFeature.cs` | Wasserwaage: zeigt den Neigungswinkel, färbt sich innerhalb der Toleranz |
| `Assets/Scripts/FaceCamera.cs` | Dreht Textanzeigen zur Kamera |
| `Assets/Prefabs/*.prefab` | Wasserwaage, Maßband, Messanzeige, Controller-Bereich |
| `Packages/com.learnxr.core` | Eingebettete Hilfsbibliothek (Singleton, Logger); nur ändern, wenn nötig |

## Branches

- **Niemals auf `main` committen oder pushen.** `main` ist der Ausgangszustand der Einführung.
- Prüfe vor jeder Änderung mit `git branch --show-current`, auf welchem Branch du bist.
- Bist du auf `main`, frag nach dem Namen der Person und lege einen Branch `prof-<name>` an
  (Kleinbuchstaben, ohne Umlaute und Leerzeichen, z. B. `prof-mueller`). Existiert er schon
  auf GitHub, wechsle auf ihn (`git fetch` und `git switch`).
- Committe und **pushe direkt auf diesen Branch**. Einen Pull Request nur, wenn ausdrücklich gewünscht.

## Regeln für Unity

- **`[SerializeField]`-Werte:** Der Wert im Code ist nur der Standard. Der wirksame Wert steht
  oft in der Szene oder im Prefab (z. B. `levelerTolerance: 5` in `LevelerFeature.prefab`).
  Suche bei jeder Wertänderung mit `grep` in `*.unity` und `*.prefab` und ändere ihn dort mit.
- **Szenen, Prefabs, Materialien** (`.unity`, `.prefab`, `.mat`) nur gezielt zeilenweise ändern.
  `guid`- und `fileID`-Werte niemals ändern, keine Objekte per Hand hinzufügen. Neue Objekte
  lieber per Code zur Laufzeit erzeugen (wie in `ToolMenu.cs`).
- **Neue Skripte** brauchen in Unity eine `.meta`-Datei – lege keine an, Unity erzeugt sie.
  Schreib stattdessen genau, an welches GameObject man das Skript hängen muss. Besser:
  bestehende Skripte erweitern, dann ist in Unity nichts zu tun.
- Unity-6-APIs verwenden: z. B. `PhysicsMaterial` (nicht `PhysicMaterial`),
  `FindFirstObjectByType` (nicht `FindObjectOfType`), `Rigidbody.linearVelocity`.
- `Packages/manifest.json` und `ProjectSettings/` nur ändern, wenn ausdrücklich gewünscht.
- Keine API-Schlüssel, Tokens oder Passwörter in Dateien schreiben.
- Halte Änderungen klein und schreib keine unnötigen Umbauten.

## Nicht verändern

- `Assets/Editor/Einfuehrung/` – der „Aktualisieren“-Button, mit dem alle den neuen Stand holen
- `AGENTS.md`, `.github/`, `.devcontainer/`

## Nach jeder Änderung

Schreib den Teilnehmenden kurz auf Deutsch:

1. **Was** du geändert hast (Dateien und in einem Satz, warum)
2. **In Unity:** *Einführung → Aktualisieren* (Branch-Name nennen) – und ob sonst etwas zu tun ist
3. **Testen auf der Brille:** *Build And Run*, dann woran man die Änderung erkennt
4. **Was schiefgehen könnte**, falls es nicht klappt

Meldet jemand einen Fehler aus der Unity-Console, behebe ihn auf demselben Branch.
