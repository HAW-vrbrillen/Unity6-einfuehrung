# MR Messwerkzeug – Unity & Meta Quest 3

Mixed-Reality-Mess-App für die Meta Quest 3: Maßband, Wasserwaage und ein Werkzeug-Menü
am Controller. Beispielprojekt für die Einführung „KI-gestützte Entwicklung von
Mixed-Reality-Szenarien in Unity“ (HAW Hamburg).

**Unity-Version:** 6000.4.6f1 (Unity 6) mit *Android Build Support*

## Projekt auf die Brille bringen

1. Auf dieser Seite **Code → Download ZIP**, entpacken (kurzer Pfad ohne Leerzeichen)
2. Unity Hub → **Add → Add project from disk** → Ordner wählen → öffnen
3. Quest per USB-C anschließen, in der Brille USB-Debugging erlauben
4. **File → Build Profiles** → Android → Quest bei *Run Device* wählen → **Build And Run**

## Mit KI verändern (Vibe Coding)

1. Auf GitHub: **Code → Codespaces → Create codespace** – öffnet VS Code im Browser
2. Rechts im **Chat** (Agent-Modus) den Wunsch beschreiben, z. B. *„Der Laser soll grün sein“*.
   Die KI legt deinen Branch `prof-<name>` an, ändert das Projekt und speichert es auf GitHub.
3. In Unity: **Einführung → Einstellungen …** → deinen Branch eintragen (einmalig)
4. **Einführung → Aktualisieren** – holt den neuen Stand, ganz ohne Git
5. **Build And Run** – und auf der Brille ansehen

Fehler in der Unity-Console? Meldung kopieren und der KI im Chat schicken.

Die KI hat dabei **keinen Zugriff auf deinen Rechner** – sie arbeitet nur in diesem Repository.
Regeln für die KI: [`AGENTS.md`](AGENTS.md)

## Lizenzen

`Packages/com.learnxr.core`: MIT-Lizenz, © Dilmer Valecillos ([learnxr.io](https://www.learnxr.io)).
Meta XR SDK: Lizenz von Meta, wird über den Unity Package Manager geladen.
