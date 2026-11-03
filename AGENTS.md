# PaceScape

Unity/C#-Projekt für AR-Lauftraining, derzeit im Aufbau. Zweck und Einstieg: [README.md](README.md).

- Der Repository-Stamm enthält die Dokumentation und Git-Konfiguration; das eigentliche Unity-Projekt liegt im Unterordner `PaceScape/`. Alle folgenden Pfade sind relativ zum Repository-Stamm.
- Unity-Version aus `PaceScape/ProjectSettings/ProjectVersion.txt` verwenden (aktuell `2022.3.62f3`). Paketabhängigkeiten stehen in `PaceScape/Packages/manifest.json`.
- Aktuell enthält das Projekt eine `SampleScene`, aber keine eigenen C#-Skripte. URP, OpenXR, Magic-Leap-SDK, BLE und RunningServer-Anbindung sind noch nicht eingerichtet. Werkzeuge und Funktionen aus MarkerML2 nicht als vorhanden voraussetzen.
- Assets über Unity verschieben oder ihre `.meta`-Dateien mitnehmen, damit GUIDs und Szenenreferenzen erhalten bleiben.
- Neue Laufzeit-Skripte im passenden Fachordner unter `PaceScape/Assets/Scripts/` ablegen. Editor-Werkzeuge gehören unter `PaceScape/Assets/Editor/`.
- `.gitattributes` und `.gitignore` im Repository-Stamm beachten. Binäre Assets gemäß den vorhandenen Regeln über Git LFS versionieren. Skripte, als Text serialisierte Unity-Assets und `.meta`-Dateien bleiben in regulärem Git.
- Generierte Unity-Ordner, lokale IDE-Dateien und Builds nicht committen. Änderungen des Nutzers erhalten; keine beiläufigen Paket-Upgrades oder Umbauten.
- Nach C#-Änderungen Unity kompilieren lassen und die Konsole auf Fehler prüfen. Relevante vorhandene Tests ausführen; projektspezifische Logikprüfungen und Geräte-Build-Werkzeuge sind hier noch nicht eingerichtet.
- Änderungen an Szenen, Oberflächen, Platzierung und Animationen zusätzlich in Unity beziehungsweise bei Hardwareintegration auf dem Zielgerät prüfen. Nicht durchgeführte Prüfungen benennen.
- Änderungen an C#-Code, Szenen oder eingebauten Assets benötigen einen neuen Geräte-Build, um in einer installierten App wirksam zu werden.

## Our Roles

- The user leads the conceptual work and makes scientific and methodological decisions.
- The agent implements individual code sections within the agreed scope.
- The agent researches and explains relevant knowledge and options to support the user's thinking.
- The agent critically examines the user's concepts and decisions for scientific soundness and explains concerns and uncertainties.
- The agent points out overlooked assumptions, missing information, and relevant considerations the user may have forgotten.
