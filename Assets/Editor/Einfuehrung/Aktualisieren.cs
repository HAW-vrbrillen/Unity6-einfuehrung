using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

namespace Einfuehrung
{
    /// <summary>
    /// Menü "Einführung → Aktualisieren": holt den aktuellen Stand eines Branches als ZIP
    /// von GitHub und übernimmt Assets, Packages und ProjectSettings ins Projekt – ohne Git.
    /// </summary>
    public static class Aktualisieren
    {
        const string ConfigPfad = "Assets/Editor/Einfuehrung/einfuehrung.json";
        static readonly string[] Ordner = { "Assets", "Packages", "ProjectSettings" };

        [Serializable]
        public class Config
        {
            public string repository = "";
            public string standardBranch = "main";
        }

        public class Ergebnis
        {
            public readonly List<string> Neu = new();
            public readonly List<string> Geaendert = new();
            public readonly List<string> Geloescht = new();
            public bool ErsterLauf;
            public int Gesamt => Neu.Count + Geaendert.Count + Geloescht.Count;
            public IEnumerable<string> Alle => Neu.Concat(Geaendert).Concat(Geloescht);
        }

        static string ProjektPfad => Path.GetDirectoryName(Application.dataPath);

        // Liste der Dateien aus dem letzten Download – damit wissen wir, welche Dateien
        // im Repo gelöscht wurden. Liegt in Library/, wird also nie mitverteilt.
        static string StandDatei => Path.Combine(ProjektPfad, "Library", "Einfuehrung", "letzter-stand.txt");

        public static Config LadeConfig()
        {
            var pfad = Path.Combine(ProjektPfad, ConfigPfad);
            return File.Exists(pfad) ? JsonUtility.FromJson<Config>(File.ReadAllText(pfad)) : new Config();
        }

        // Branch und Token sind pro Person – deshalb in den EditorPrefs, nicht im Projekt.
        static string Schluessel(string name) => $"Einfuehrung.{LadeConfig().repository}.{name}";

        public static string Branch
        {
            get => EditorPrefs.GetString(Schluessel("Branch"), LadeConfig().standardBranch);
            set => EditorPrefs.SetString(Schluessel("Branch"), value.Trim());
        }

        public static string Token
        {
            get => EditorPrefs.GetString(Schluessel("Token"), "");
            set => EditorPrefs.SetString(Schluessel("Token"), value.Trim());
        }

        [MenuItem("Einführung/Aktualisieren", priority = 0)]
        static void MenueAktualisieren()
        {
            var config = LadeConfig();
            if (string.IsNullOrEmpty(config.repository))
            {
                EditorUtility.DisplayDialog("Aktualisieren", $"In {ConfigPfad} ist kein Repository eingetragen.", "OK");
                return;
            }

            var branch = Branch;
            if (!EditorUtility.DisplayDialog("Projekt aktualisieren",
                    $"Neuesten Stand von Branch „{branch}“ holen?\n\nRepository: {config.repository}\n\n" +
                    "Änderungen, die du selbst in Unity gemacht hast, werden dabei überschrieben.",
                    "Aktualisieren", "Abbrechen"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var offeneSzenen = Enumerable.Range(0, EditorSceneManager.sceneCount)
                .Select(i => EditorSceneManager.GetSceneAt(i).path)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            Ergebnis ergebnis;
            try
            {
                ergebnis = Ausfuehren(config.repository, branch, Token);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Aktualisieren fehlgeschlagen", e.Message, "OK");
                return;
            }

            if (ergebnis.Gesamt == 0)
            {
                EditorUtility.DisplayDialog("Aktualisieren", $"Schon aktuell – Branch „{branch}“ hat keine neuen Änderungen.", "OK");
                return;
            }

            // Geänderte Szenen neu laden, damit man sofort den neuen Stand sieht.
            var geaenderteSzene = offeneSzenen.FirstOrDefault(p => ergebnis.Alle.Contains(p));
            if (geaenderteSzene != null && File.Exists(Path.Combine(ProjektPfad, geaenderteSzene)))
                EditorSceneManager.OpenScene(geaenderteSzene);

            var meldung = $"Branch „{branch}“ übernommen:\n" +
                          $"{ergebnis.Neu.Count} neu, {ergebnis.Geaendert.Count} geändert, {ergebnis.Geloescht.Count} gelöscht\n\n" +
                          string.Join("\n", ergebnis.Alle.Where(p => !p.EndsWith(".meta")).Take(15));
            if (ergebnis.ErsterLauf)
                meldung += "\n\n(Erster Lauf: Dateien, die im Repo gelöscht wurden, werden erst ab dem nächsten Mal entfernt.)";

            // ProjectSettings liest Unity nur beim Start zuverlässig ein.
            if (ergebnis.Alle.Any(p => p.StartsWith("ProjectSettings/")))
            {
                if (EditorUtility.DisplayDialog("Aktualisieren", meldung + "\n\nDie Projekteinstellungen haben sich geändert. Unity jetzt neu starten?",
                        "Neu starten", "Später"))
                    EditorApplication.OpenProject(ProjektPfad);
                return;
            }

            EditorUtility.DisplayDialog("Aktualisieren", meldung + "\n\nDanach: Build And Run, um es auf der Brille zu sehen.", "OK");
        }

        [MenuItem("Einführung/Einstellungen …", priority = 1)]
        static void MenueEinstellungen() => EinstellungenFenster.Oeffnen();

        [MenuItem("Einführung/Branch auf GitHub öffnen", priority = 20)]
        static void MenueGitHub() => Application.OpenURL($"https://github.com/{LadeConfig().repository}/tree/{Branch}");

        /// <summary>Lädt den Branch und gleicht die Projektordner ab. Ohne Dialoge, damit testbar.</summary>
        public static Ergebnis Ausfuehren(string repository, string branch, string token)
        {
            var zipPfad = Path.Combine(Path.GetTempPath(), $"einfuehrung-{Guid.NewGuid():N}.zip");
            try
            {
                Herunterladen(repository, branch, token, zipPfad);
                EditorUtility.DisplayProgressBar("Aktualisieren", "Dateien übernehmen …", 1f);
                var ergebnis = Uebernehmen(zipPfad);

                AssetDatabase.Refresh();
                if (ergebnis.Alle.Any(p => p.StartsWith("Packages/")))
                    UnityEditor.PackageManager.Client.Resolve();
                return ergebnis;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (File.Exists(zipPfad)) File.Delete(zipPfad);
            }
        }

        static void Herunterladen(string repository, string branch, string token, string zielPfad)
        {
            // Öffentliche Repos direkt über codeload, private über die API mit Zugangstoken.
            var url = string.IsNullOrEmpty(token)
                ? $"https://codeload.github.com/{repository}/zip/refs/heads/{branch}"
                : $"https://api.github.com/repos/{repository}/zipball/{branch}";

            using var anfrage = UnityWebRequest.Get(url);
            anfrage.downloadHandler = new DownloadHandlerFile(zielPfad) { removeFileOnAbort = true };
            anfrage.SetRequestHeader("User-Agent", "Unity-Einfuehrung");
            if (!string.IsNullOrEmpty(token))
                anfrage.SetRequestHeader("Authorization", "Bearer " + token);

            var vorgang = anfrage.SendWebRequest();
            while (!vorgang.isDone)
            {
                var mb = anfrage.downloadedBytes / (1024f * 1024f);
                if (EditorUtility.DisplayCancelableProgressBar("Aktualisieren", $"Lade „{branch}“ … {mb:0.0} MB", 0.5f))
                {
                    anfrage.Abort();
                    throw new OperationCanceledException("Abgebrochen.");
                }
                Thread.Sleep(50);
            }

            if (anfrage.result == UnityWebRequest.Result.Success)
                return;

            if (anfrage.responseCode == 404)
                throw new Exception($"Branch „{branch}“ wurde in {repository} nicht gefunden.\n\n" +
                                    "Ist der Name richtig geschrieben (Einführung → Einstellungen)? " +
                                    "Bei einem privaten Repository muss dort außerdem ein Zugangstoken eingetragen sein.");
            if (anfrage.responseCode == 401 || anfrage.responseCode == 403)
                throw new Exception("Kein Zugriff auf das Repository. Bitte den Zugangstoken unter Einführung → Einstellungen prüfen.");
            throw new Exception($"Download fehlgeschlagen: {anfrage.error}\n\nBesteht eine Internetverbindung?");
        }

        static Ergebnis Uebernehmen(string zipPfad)
        {
            var ergebnis = new Ergebnis();
            var neuerStand = new HashSet<string>();

            using (var zip = ZipFile.OpenRead(zipPfad))
            {
                foreach (var eintrag in zip.Entries)
                {
                    if (string.IsNullOrEmpty(eintrag.Name))
                        continue; // Ordner

                    // GitHub packt alles in einen Oberordner "repo-branch/".
                    var schnitt = eintrag.FullName.IndexOf('/');
                    if (schnitt < 0)
                        continue;
                    var relativ = eintrag.FullName.Substring(schnitt + 1);
                    if (!Ordner.Any(o => relativ.StartsWith(o + "/")))
                        continue;

                    neuerStand.Add(relativ);
                    var ziel = Path.Combine(ProjektPfad, relativ);
                    var inhalt = Lesen(eintrag);

                    if (File.Exists(ziel))
                    {
                        if (File.ReadAllBytes(ziel).AsSpan().SequenceEqual(inhalt))
                            continue;
                        ergebnis.Geaendert.Add(relativ);
                    }
                    else
                    {
                        ergebnis.Neu.Add(relativ);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(ziel));
                    File.WriteAllBytes(ziel, inhalt);
                }
            }

            if (neuerStand.Count == 0)
                throw new Exception("Das heruntergeladene Archiv enthält keine Projektdateien.");

            // Nur Dateien löschen, die beim letzten Mal aus dem Repo kamen und jetzt fehlen.
            // So bleiben Dateien, die Unity lokal erzeugt, unangetastet.
            if (File.Exists(StandDatei))
            {
                foreach (var relativ in File.ReadAllLines(StandDatei).Where(p => p.Length > 0 && !neuerStand.Contains(p)))
                {
                    var ziel = Path.Combine(ProjektPfad, relativ);
                    if (!File.Exists(ziel))
                        continue;
                    File.Delete(ziel);
                    ergebnis.Geloescht.Add(relativ);
                    LeereOrdnerEntfernen(Path.GetDirectoryName(ziel));
                }
            }
            else
            {
                ergebnis.ErsterLauf = true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StandDatei));
            File.WriteAllLines(StandDatei, neuerStand.OrderBy(p => p));
            return ergebnis;
        }

        static byte[] Lesen(ZipArchiveEntry eintrag)
        {
            using var quelle = eintrag.Open();
            using var puffer = new MemoryStream();
            quelle.CopyTo(puffer);
            return puffer.ToArray();
        }

        static void LeereOrdnerEntfernen(string ordner)
        {
            var wurzeln = Ordner.Select(o => Path.GetFullPath(Path.Combine(ProjektPfad, o))).ToList();
            var aktuell = Path.GetFullPath(ordner);
            while (!wurzeln.Contains(aktuell) && Directory.Exists(aktuell) && !Directory.EnumerateFileSystemEntries(aktuell).Any())
            {
                Directory.Delete(aktuell);
                var meta = aktuell + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
                aktuell = Path.GetDirectoryName(aktuell);
            }
        }
    }

    /// <summary>Fenster für Branch-Namen und (bei privaten Repos) Zugangstoken.</summary>
    public class EinstellungenFenster : EditorWindow
    {
        string branch;
        string token;

        public static void Oeffnen()
        {
            var fenster = GetWindow<EinstellungenFenster>(true, "Einführung – Einstellungen");
            fenster.minSize = new Vector2(460, 230);
        }

        void OnEnable()
        {
            branch = Aktualisieren.Branch;
            token = Aktualisieren.Token;
        }

        void OnGUI()
        {
            var config = Aktualisieren.LadeConfig();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Repository", config.repository);
            EditorGUILayout.Space();

            branch = EditorGUILayout.TextField("Mein Branch", branch);
            EditorGUILayout.HelpBox("Der Branch, auf dem die KI deine Änderungen speichert, z. B. „prof-mueller“. " +
                                    $"Standard: „{config.standardBranch}“.", MessageType.None);

            token = EditorGUILayout.PasswordField("Zugangstoken (optional)", token);
            EditorGUILayout.HelpBox("Nur bei einem privaten Repository nötig: ein GitHub-Token mit Lesezugriff " +
                                    "(Fine-grained, nur dieses Repository, Berechtigung „Contents: Read“).", MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("Speichern"))
            {
                Aktualisieren.Branch = string.IsNullOrWhiteSpace(branch) ? config.standardBranch : branch;
                Aktualisieren.Token = token ?? "";
                Close();
            }
        }
    }
}
