// Edits the project's WebSocket endpoint and reconnects the client in Play Mode.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PaceScape.RemoteControl;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PaceScape.Editor
{
    // Edits the build defaults and reconnects the current Editor client on demand.
    public sealed class RemoteControlSettingsWindow : EditorWindow
    {
        private const string AssetPath = "Assets/Resources/PaceScapeConnection.asset";
        private const string StylePath = "Assets/Editor/RemoteControlSettingsWindow.uss";
        private Toggle _connectionEnabled;
        private Toggle _allowLanMotorControl;
        private TextField _serverUrl;
        private TextField _accessToken;
        private Button _reconnectButton;
        private HelpBox _status;
        private DropdownField _localAddressChoice;
        private readonly List<LocalAddress> _localAddresses = new List<LocalAddress>();

        private sealed class LocalAddress
        {
            public string Address;
            public string Label;
            public bool HasGateway;
        }

        [MenuItem("PaceScape/Serververbindung")]
        private static void OpenWindow()
        {
            var window = GetWindow<RemoteControlSettingsWindow>();
            window.titleContent = new GUIContent("Serververbindung");
            window.minSize = new Vector2(440f, 390f);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        // Builds a native Editor settings form using UI Toolkit.
        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.AddToClassList("connection-settings");
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }

            var scroll = new ScrollView();
            root.Add(scroll);
            var heading = new Label("PaceScape · Serververbindung");
            heading.AddToClassList("heading");
            scroll.Add(heading);
            AddDescription(scroll, "Hier legst du die Verbindung für den Unity Editor und den nächsten App-Build fest.");

            RemoteControlSettings settings = AssetDatabase.LoadAssetAtPath<RemoteControlSettings>(AssetPath);
            _connectionEnabled = new Toggle("Verbindung aktivieren")
            {
                value = settings == null || settings.ConnectionEnabled
            };
            _serverUrl = new TextField("Serveradresse")
            {
                value = settings != null ? settings.ServerUrl : RemoteControlSettings.DefaultServerUrl,
                tooltip = "Vollständige WebSocket-Adresse, z. B. ws://192.168.1.42:8787/ws"
            };
            _accessToken = new TextField("Zugriffsschlüssel")
            {
                value = settings != null ? settings.AccessToken : string.Empty,
                isPasswordField = true,
                tooltip = "Optional; muss bei LAN-Betrieb dem ACCESS_TOKEN des Servers entsprechen."
            };
            _allowLanMotorControl = new Toggle("Laufbandsteuerung im WLAN erlauben")
            {
                value = settings != null && settings.AllowLanMotorControl,
                tooltip = "Nur für den eigenen PaceScape-Server in einem vertrauenswürdigen Netz. Der Server muss die WLAN-Steuerung ausdrücklich freigeben."
            };
            scroll.Add(_connectionEnabled);
            scroll.Add(_serverUrl);
            var detectAddress = new Button(UseLocalComputerAddress) { text = "Rechner-IP übernehmen" };
            detectAddress.tooltip = "Liest die IPv4-Adressen dieses Rechners. Hier muss der Server laufen; es wird kein Netzwerk durchsucht.";
            scroll.Add(detectAddress);
            _localAddressChoice = new DropdownField("Netzwerkadresse");
            _localAddressChoice.AddToClassList("local-address-choice");
            _localAddressChoice.SetEnabled(false);
            _localAddressChoice.RegisterValueChangedCallback(change =>
            {
                foreach (LocalAddress address in _localAddresses)
                {
                    if (address.Label == change.newValue)
                    {
                        SetServerHost(address.Address);
                        break;
                    }
                }
            });
            scroll.Add(_localAddressChoice);
            scroll.Add(_allowLanMotorControl);
            scroll.Add(_accessToken);
            AddDescription(scroll, "USB: ws://127.0.0.1:8787/ws mit adb reverse. WLAN: die IP-Adresse des Rechners statt localhost eintragen; Server im LAN-Modus starten.");
            AddDescription(scroll, "Der Schlüssel wird im Projekt und im App-Build gespeichert. Auf der Brille hat eine vorhandene pacescape-control.json Vorrang; im Editor gelten diese Projekteinstellungen.");

            var actions = new VisualElement();
            actions.AddToClassList("actions");
            var saveButton = new Button(() => SaveSettings()) { text = "Im Projekt speichern" };
            _reconnectButton = new Button(ReconnectInPlayMode) { text = "Speichern & neu verbinden" };
            actions.Add(saveButton);
            actions.Add(_reconnectButton);
            scroll.Add(actions);
            _status = new HelpBox("Änderungen für die Brille benötigen einen neuen App-Build. Im Play Mode kannst du sie direkt übernehmen; ein laufender Lauf wird dabei pausiert.", HelpBoxMessageType.Info);
            scroll.Add(_status);
            UpdateReconnectButton();
        }

        private static void AddDescription(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("description");
            parent.Add(label);
        }

        // Offers active local IPv4 addresses and preserves the configured port.
        private void UseLocalComputerAddress()
        {
            _localAddresses.Clear();
            try
            {
                var seen = new HashSet<string>();
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    bool hasGateway = false;
                    foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                    {
                        hasGateway |= gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.Any.Equals(gateway.Address);
                    }

                    foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                    {
                        IPAddress address = unicast.Address;
                        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) ||
                            IPAddress.Any.Equals(address))
                        {
                            continue;
                        }

                        byte[] bytes = address.GetAddressBytes();
                        if (bytes[0] == 169 && bytes[1] == 254)
                        {
                            continue;
                        }

                        string ip = address.ToString();
                        if (seen.Add(ip))
                        {
                            _localAddresses.Add(new LocalAddress
                            {
                                Address = ip,
                                Label = $"{adapter.Name} — {ip}",
                                HasGateway = hasGateway
                            });
                        }
                    }
                }

                _localAddresses.Sort((first, second) =>
                {
                    int gatewayOrder = second.HasGateway.CompareTo(first.HasGateway);
                    return gatewayOrder != 0 ? gatewayOrder : string.CompareOrdinal(first.Label, second.Label);
                });

                if (_localAddresses.Count == 0)
                {
                    _localAddressChoice.choices = new List<string>();
                    _localAddressChoice.SetEnabled(false);
                    ShowStatus("Keine aktive IPv4-Netzwerkadresse gefunden. WLAN/LAN prüfen oder die Serveradresse manuell eintragen. Für USB bleibt 127.0.0.1 richtig.", HelpBoxMessageType.Warning);
                    return;
                }

                var choices = new List<string>();
                LocalAddress selected = _localAddresses[0];
                RemoteControlSettings.TryParseServerUrl(_serverUrl.value, out Uri current);
                foreach (LocalAddress address in _localAddresses)
                {
                    choices.Add(address.Label);
                    if (address.Address == current?.Host)
                    {
                        selected = address;
                    }
                }

                _localAddressChoice.choices = choices;
                _localAddressChoice.SetValueWithoutNotify(selected.Label);
                _localAddressChoice.SetEnabled(_localAddresses.Count > 1);
                SetServerHost(selected.Address);
            }
            catch (Exception exception)
            {
                _localAddressChoice.SetEnabled(false);
                ShowStatus($"Rechner-IP konnte nicht gelesen werden: {exception.Message}", HelpBoxMessageType.Error);
            }
        }

        private void SetServerHost(string address)
        {
            if (!RemoteControlSettings.TryParseServerUrl(_serverUrl.value, out Uri current))
            {
                current = new Uri(RemoteControlSettings.DefaultServerUrl);
            }

            var endpoint = new UriBuilder(current) { Host = address };
            _serverUrl.SetValueWithoutNotify(endpoint.Uri.AbsoluteUri);
            ShowStatus("Lokale Rechner-IP übernommen; Port und /ws bleiben erhalten. Bei mehreren Adaptern das Netz der Brille auswählen. Danach speichern. Der Server muss auf diesem Rechner im LAN-Modus laufen; die Erreichbarkeit wurde nicht geprüft.", HelpBoxMessageType.Info);
        }

        // Validates the form before saving defaults to a Resources asset.
        private bool SaveSettings()
        {
            if (!RemoteControlSettings.TryParseServerUrl(_serverUrl.value, out Uri uri))
            {
                ShowStatus("Bitte eine gültige ws://- oder wss://-Adresse mit /ws eingeben, z. B. ws://192.168.1.42:8787/ws. Keine Zugangsdaten oder zusätzlichen URL-Parameter verwenden.", HelpBoxMessageType.Error);
                return false;
            }

            try
            {
                RemoteControlSettings settings = AssetDatabase.LoadAssetAtPath<RemoteControlSettings>(AssetPath);
                if (settings == null)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(AssetPath) != null)
                    {
                        ShowStatus("Am vorgesehenen Speicherort liegt bereits ein anderes Asset. Es wurde nicht überschrieben.", HelpBoxMessageType.Error);
                        return false;
                    }

                    if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    {
                        AssetDatabase.CreateFolder("Assets", "Resources");
                    }

                    settings = CreateInstance<RemoteControlSettings>();
                    settings.TryConfigure(_connectionEnabled.value, uri.AbsoluteUri, _accessToken.value);
                    settings.AllowLanMotorControl = _allowLanMotorControl.value;
                    AssetDatabase.CreateAsset(settings, AssetPath);
                }
                else
                {
                    Undo.RecordObject(settings, "Change PaceScape server connection");
                    settings.TryConfigure(_connectionEnabled.value, uri.AbsoluteUri, _accessToken.value);
                    settings.AllowLanMotorControl = _allowLanMotorControl.value;
                    EditorUtility.SetDirty(settings);
                }

                AssetDatabase.SaveAssetIfDirty(settings);
                _serverUrl.SetValueWithoutNotify(settings.ServerUrl);
                ShowStatus("Verbindungseinstellungen gespeichert. Sie gelten beim nächsten Play-Start und im nächsten App-Build.", HelpBoxMessageType.Info);
                return true;
            }
            catch (Exception exception)
            {
                ShowStatus($"Speichern fehlgeschlagen: {exception.Message}", HelpBoxMessageType.Error);
                return false;
            }
        }

        // Saves the form and reloads the active client's connection settings.
        private void ReconnectInPlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            var client = FindObjectOfType<RemoteControlClient>(true);
            if (client == null || !client.isActiveAndEnabled)
            {
                ShowStatus("Im Play Mode ist kein aktiver PaceScape-Verbindungsclient vorhanden. Bitte die Tracking-Szene öffnen.", HelpBoxMessageType.Warning);
                return;
            }

            if (SaveSettings())
            {
                client.ReloadConnectionSettings();
                ShowStatus("Einstellungen gespeichert und Neuverbindung angefordert. Ein laufender Lauf bleibt pausiert; den Verbindungsstatus im Dashboard prüfen.", HelpBoxMessageType.Info);
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            UpdateReconnectButton();
        }

        private void UpdateReconnectButton()
        {
            _reconnectButton?.SetEnabled(EditorApplication.isPlaying);
        }

        private void ShowStatus(string message, HelpBoxMessageType type)
        {
            _status.text = message;
            _status.messageType = type;
        }
    }
}
