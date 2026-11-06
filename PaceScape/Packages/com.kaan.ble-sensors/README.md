# BLE Sensors (Velorexe) — 1.0.0

Eigenes Unity-UPM-Package für profilbasierte BLE-Sensoren auf Android. Erstellt und mit Unity 2022.3.62f3 kompiliert. Die Magic-Leap-Szene verwendet weiterhin ihre vorhandenen XR-Eingaben.

## In diesem Projekt bereits eingebunden

Das Paket liegt als **Embedded Package** unter `Packages/com.kaan.ble-sensors`. Unity erkennt diesen Paketordner automatisch. Im Package Manager unter **In Project** nach **BLE Sensors (Velorexe)** suchen. Kein zusätzlicher Import derselben Dateien unter Assets nötig.

Die Script- und JAR-Metadaten wurden beim Verschieben erhalten. Dein Canvas, das bearbeitbare Profil und die projektspezifischen Canvas-Werkzeuge bleiben unter `Assets/HeartRateBLE`. `Assets/Gestures.cs` sowie die Magic-Leap-Konfiguration bleiben Teil des Projekts. Das Package installiert kein neues XR-Rig und verändert keine Szene automatisch.

## Struktur

```text
com.kaan.ble-sensors/
  package.json                       Name, Version und Abhängigkeiten
  Runtime/
    Kaan.BleSensors.Runtime.asmdef    Eigene Runtime-Assembly
    BleConnection.cs                 Allgemeine BLE-Verbindung
    BleDeviceProfile.cs              Kanaldefinitionen und ältere Filterfelder
    BleGattQueue.cs                  Serielle GATT-Aufträge
    AndroidBleGattBackend.cs         Verbindung zum nativen JAR
    SensorMeasurements.cs           Auswertung der Profilkanäle
    HeartRateParser.cs               Herzfrequenz und RR
    CosinussParsers.cs               Weitere Datenformate
    CosinussProfileDefaults.cs       Standardprofil
    HeartRateUI.cs                   Anbindung einer uGUI-Oberfläche
    HeartRateBLE.cs                  Kompatibilität zu bisherigen Szenen
    CosinussMeasurements.cs          Kompatibilität zu bisherigen Szenen
    Plugins/Android/cosinuss-gatt.jar
  Editor/
    Kaan.BleSensors.Editor.asmdef
    BleProfileMenu.cs                Neues cosinuss-Profil erstellen
  Documentation~/
    Modular.md                       Architektur und Inspector-Einstellungen
    AndroidManifest-BLE.xml          BLE-Einträge zum Zusammenführen
```

## In einem anderen Unity-Projekt installieren

1. Unity **2022.3** verwenden; die tatsächliche Prüfung erfolgte mit 2022.3.62f3.
2. **Window → Package Manager → + → Add package from git URL** öffnen und zuerst das geprüfte Velorexe-Plugin einfügen:

```text
https://github.com/Velorexe/Unity-Android-Bluetooth-Low-Energy.git#55d8c12e155cc45bc26cea120d5e8c1e5f231b9f
```

3. Anschließend **+ → Add package from tarball** wählen und `com.kaan.ble-sensors-1.0.0.tgz` auswählen. Alternativ das Package in einen dauerhaften Ordner entpacken, **Add package from disk** wählen und dessen `package.json` öffnen. Eine Verknüpfung vom Datenträger hängt von diesem Ordner ab; ihn danach nicht verschieben.
4. Unity löst TextMeshPro 3.0.7, uGUI 1.0.0 und Android-JNI über die Paketabhängigkeiten auf. Das Git-Plugin muss zuerst hinzugefügt werden: Die Paketabhängigkeit kann seine Git-URL nicht automatisch bereitstellen.
5. **Assets → Create → BLE → Cosinuss-Standardprofil** wählen und das Profil im Assets-Ordner speichern. Für andere Geräte **Create → BLE → Geräteprofil** verwenden. Datenkanäle und UUIDs im Inspector einstellen. Die Gerätesuche zeigt alle gefundenen BLE-Geräte; gespeicherte Namens-/Kennungsfilter beschränken die Liste nicht mehr.
6. Auf einem eigenen GameObject **BleConnection** und **SensorMeasurements** hinzufügen. Das Profil bei BleConnection zuweisen, den Verbindungsbaustein bei SensorMeasurements im Feld Bluetooth zuweisen.
7. Für die mitgelieferte UI-Anbindung eine uGUI-Oberfläche mit vier Buttons, TMP-Dropdown und TMP-Texten verwenden. HeartRateUI hinzufügen: Bluetooth → BleConnection; Sensor → SensorMeasurements; Devices → Dropdown; Status Text → Statusanzeige; Heart Rate Text → Hauptmesswert; Sensor Details Text → Zusatzwerte; die vier Button-Felder → Scan/Stop/Connect/Disconnect. TMP Essential Resources bei Aufforderung importieren.
8. Die BLE-Einträge aus `Documentation~/AndroidManifest-BLE.xml` mit dem eigenen Android-Manifest zusammenführen. Nicht das gesamte vorhandene Manifest ersetzen. Die Laufzeitabfragen erfolgen im Code.
9. Für Magic Leap zusätzlich das eigene OpenXR-/Handtracking-Setup, World-Space-Canvas, XRUIInputModule und TrackedDeviceGraphicRaycaster konfigurieren. Diese projektabhängigen Teile werden nicht durch dieses BLE-Package installiert.
10. Android-Build auf dem Gerät testen. Der Unity-Editor führt keinen BLE-Funkscan aus.

Vorhandene alte Kopien dieser Skripte/JARs unter Assets dürfen in einem Zielprojekt nicht gleichzeitig mit dem Package existieren, sonst entstehen doppelte Typen oder native Klassen. Im aktuellen MarkerML2-Projekt wurden sie bereits verschoben.

## Abhängigkeiten und Grenzen

Velorexe bleibt erforderlich: Paketversion **0.0.6**, geprüfte Git-Revision **55d8c12e155cc45bc26cea120d5e8c1e5f231b9f**. Das native Companion-JAR verwendet ein privates Feld dieser Plugin-Version. Ein Plugin-Update muss daher erneut geprüft werden. Das JAR wurde unverändert übernommen; das frühere Java-Quell-/Buildpaket war zum Zeitpunkt dieser Paketmigration nicht mehr im Projekt vorhanden.

Ein Geräteprofil garantiert keine Unterstützung beliebiger Geräteprotokolle. Unbekannte Formate brauchen einen Decoder. Kein allgemeiner Schreibdialog, keine individuellen Unsubscribe-Schalter. Grenzen bei autoConnect-Abbruch und DescriptorWrite-Status bleiben bestehen. Bei aktivierter Android-Minifizierung müssen die Companion-Klasse und die angesprochenen Plugin-Klassen/Felder erhalten bleiben.

## Prüfstand

- Runtime für Android und Editor kompiliert; Editor-Werkzeuge getrennt dagegen kompiliert.
- 34 automatisierte Profil-/Queue-/Parser-Prüfungen erfolgreich.
- Script-GUIDs und JAR-Hash beim Verschieben erhalten.
- Die tatsächliche Unity-Paket-/Prefab-Prüfung des Arbeitsprojekts wird in `BLE-Delivery/Package-Ergebnis.txt` protokolliert.
- Kein neuer Hardwaretest und kein frischer Android-Build allein durch diese Paketmigration.

[Unity: Eigene Packages erstellen](https://docs.unity3d.com/2022.3/Documentation/Manual/CustomPackages.html)

## Fernbedienung über RunningServer

Unter **Sensoren & Session** im Dashboard suchen, ein Gerät anhand von Name/Kennung auswählen und verbinden oder trennen. Die Magic Leap führt den Scan und die Verbindung aus; einmalige Android-Berechtigungen bleiben auf der Brille. Das Profil beschreibt weiterhin die auslesbaren Datenkanäle, nicht die Suchliste. Geräte ohne passende Kanäle können verbunden sein, ohne interpretierbare Messwerte zu liefern.

Die Fernsuche aktiviert nur Transport und Messwertauswertung im Hintergrund. Das alte Sensorfenster bleibt unsichtbar und verdrängt das Lauf-HUD nicht. Nach der Plugin-Initialisierung wartet die Suche auf die aktive Brillen-App, damit ein kurzer Android-Berechtigungsdialog den Scan nicht sofort beendet. Vor jedem Scan wird die native Geräteliste geleert, damit auch bereits zuvor gefundene Geräte erneut gemeldet werden.

Wenn die Suche nicht starten kann, meldet die App ausgeschaltetes Bluetooth beziehungsweise auf Android bis Version 11 deaktivierte Standortdienste ausdrücklich. Eine leere abgeschlossene Suche bedeutet, dass keine BLE-Werbesignale empfangen wurden; sie ist keine Liste sämtlicher Bluetooth-Classic-Geräte oder bereits gekoppelter Geräte.

Herzfrequenz, Temperatur in Celsius, SpO₂, Puls und Perfusionsindex stehen als numerische Getter für Telemetrie bereit. Es werden nur gültige, gemäß Profil aktuelle Werte einer aktiven Verbindung geliefert. Das Dashboard zeigt das verbundene Gerät separat von der Suchliste an; fehlende oder veraltete Werte bleiben leer.

## FTMS-Laufbänder

Wenn das verbundene Gerät Service **1826** und Characteristic **2ACD** anbietet, ergänzt `SensorMeasurements` die Sitzungskopie des Geräteprofils automatisch um einen FTMS-Notify-Kanal. Das ursprüngliche Profil-Asset und die ungefilterte Suchliste werden nicht verändert. `FtmsTreadmillData` liest die Momentangeschwindigkeit in 0,01 km/h und optional Total Distance als UINT24 in Metern (Flags-Bit 2, nach dem optionalen Average-Speed-Feld). `TreadmillDistanceReceived` meldet neue Distanzpakete, `TryGetTreadmillTotalDistanceMetres` stellt den maximal zwei Sekunden alten Rohzähler bereit. Distanz-only-Teilpakete erneuern keinen Geschwindigkeitszeitstempel. Die Session-Auswertung bleibt im Lauftraining außerhalb des BLE-Pakets. `TreadmillSpeedReceived` meldet nur neue Geschwindigkeitspakete. `TryGetTreadmillSpeedKmh` liefert nur aktuelle Werte; `TreadmillSignalLost` meldet Trennung/ungültige Pakete. Der Enum-Wert wurde am Ende angehängt, damit vorhandene Profil-Assets ihre Bedeutung behalten.

Optional lässt sich über **Assets → Create → BLE → Laufband FTMS** ein eigenes Profil erstellen. Die Projekt-Komponente `TreadmillBleAdapter` bindet diese Daten an die Laufwelt. Keine Motor-/Steigungssteuerung und derzeit nur eine BLE-Verbindung gleichzeitig. Weitere Anleitung: `Docs/TreadmillConnection.md` im Projekt.
