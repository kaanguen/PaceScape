using System;
using System.Collections;
using System.Collections.Generic;
using Android.BLE;
using Android.BLE.Commands;
using UnityEngine;
using UnityEngine.Android;

public class BleConnection : MonoBehaviour
{
    [Tooltip("Datenkanäle. Der Scan zeigt unabhängig vom Profil alle Geräte. Nur im getrennten Zustand wechseln.")]
    public BleDeviceProfile profile;
    BleDeviceProfile fallbackProfile;
    public BleDeviceProfile Profile => profile != null ? profile : (fallbackProfile != null ? fallbackProfile : (fallbackProfile = CreateFallbackProfile()));
    public BleDeviceProfile ActiveProfile { get; private set; }
    protected virtual BleDeviceProfile CreateFallbackProfile() => ScriptableObject.CreateInstance<BleDeviceProfile>();
    BleGattQueue operations;
    public event Action<string,string,byte[]> DataReceived;
    public event Action<string,string,bool,string> OperationCompleted;

    void EnsureOperations()
    {
        if (operations != null) return;
        operations = new BleGattQueue(new AndroidBleGattBackend());
        operations.Failed += OnError;
        operations.Completed += (op, data, error) => {
            if (!Connected) return;
            if (!op.Subscribe && error == null) DataReceived?.Invoke(op.Service, op.Characteristic, data);
            OperationCompleted?.Invoke(op.Service, op.Characteristic, op.Subscribe, error);
        };
    }
    public void Read(string service, string characteristic) => Enqueue(service,characteristic,false,1);
    public void Subscribe(string service,string characteristic,BleUpdateMode mode = BleUpdateMode.NotifyPreferred) => Enqueue(service,characteristic,true,(int)mode);
    void Enqueue(string service,string characteristic,bool subscribe,int mode)
    {
        if (!Connected) throw new InvalidOperationException("Keine Verbindung.");
        if (!HasCharacteristic(service,characteristic)) throw new InvalidOperationException("Datenkanal nicht entdeckt.");
        EnsureOperations();
        operations.Enqueue(new BleGattQueue.Request { Address=connectedId,Service=service,Characteristic=characteristic,Subscribe=subscribe,Mode=mode });
    }
    [Range(3, 60)] public int scanSeconds = 15;
    [Tooltip("Velorexe verwendet Android autoConnect. Der erste Verbindungsaufbau kann länger als 30 Sekunden dauern.")]
    [Range(30, 180)] public int connectTimeoutSeconds = 90;

    public event Action Changed;
    public event Action<string> GattReady;
    public event Action<string> GattError;
    readonly HashSet<string> foundCharacteristics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string ConnectedDeviceId => connectedId;
    public string ConnectedDeviceName
    {
        get
        {
            if (string.IsNullOrEmpty(connectedId)) return null;
            int index = DeviceIds.FindIndex(id => string.Equals(id, connectedId, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < DeviceNames.Count ? DeviceNames[index] : "Ohne Namen";
        }
    }
    public bool HasCharacteristic(string service, string characteristic) => foundCharacteristics.Contains(BleUuid.Key(service, characteristic));
    public void ShowStatus(string text) { SetStatus(text); }
    public readonly List<string> DeviceIds = new List<string>();
    public readonly List<string> DeviceNames = new List<string>();
    public string Status { get; private set; } = "Bereit. Scan drücken.";
    public bool Scanning { get; private set; }
    public bool Busy { get; private set; }
    public bool Initializing => requesting;
    public bool Connected { get; private set; }
    public int SelectedIndex { get; private set; }

    BleManager manager;
    BleAdapter adapter;
    DiscoverDevices scan;
    ConnectToDevice connection;
    string connectedId;
    bool requesting, shuttingDown, gattOpen, applicationPaused;
    float connectionDeadline, scanRestartAfter, scanFocusReadyAfter;

    void Notify() { Changed?.Invoke(); }
    void SetStatus(string text) { Status = text; Debug.Log("[BLE] " + text); Notify(); }

    public void StartScan()
    {
        if (requesting || Busy || Connected || Scanning) return;
        if (Time.realtimeSinceStartup < scanRestartAfter)
        {
            SetStatus("Vor erneutem Scan noch kurz warten (Plugin-Scan-Timer).");
            return;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(InitializeAndScan());
#else
        SetStatus("BLE funktioniert hier nur im Android-Build auf der Brille.");
#endif
    }

    public bool TryStartScan(out string error)
    {
        error = null;
        if (requesting || Busy || Connected || Scanning)
        { error = "Suche oder Verbindung bereits aktiv. Gegebenenfalls zuerst trennen."; return false; }
        StartScan();
        if (requesting || Scanning) return true;
        error = Status;
        return false;
    }

    IEnumerator InitializeAndScan()
    {
        requesting = true;
        if (manager == null)
        {
            int api;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                api = version.GetStatic<int>("SDK_INT");
            string[] permissions = api >= 31
                ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
                : new[] { "android.permission.ACCESS_FINE_LOCATION" };

            foreach (string permission in permissions)
            {
                if (Permission.HasUserAuthorizedPermission(permission)) continue;
                SetStatus("Bitte Berechtigung erlauben: " + permission);
                bool answered = false;
                var callbacks = new PermissionCallbacks();
                Action<string> answer = _ => answered = true;
                callbacks.PermissionGranted += answer;
                callbacks.PermissionDenied += answer;
                callbacks.PermissionDeniedAndDontAskAgain += answer;
                Permission.RequestUserPermission(permission, callbacks);
                float until = Time.realtimeSinceStartup + 90;
                while (!answered && Time.realtimeSinceStartup < until) yield return null;
                callbacks.PermissionGranted -= answer;
                callbacks.PermissionDenied -= answer;
                callbacks.PermissionDeniedAndDontAskAgain -= answer;
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    requesting = false;
                    SetStatus("Berechtigung fehlt. In den Android-App-Einstellungen freigeben.");
                    yield break;
                }
            }
            try
            {
                // Do not place another BleManager or the upstream sample in this scene.
                manager = BleManager.Instance;
                adapter = FindObjectOfType<BleAdapter>();
                if (adapter == null) throw new InvalidOperationException("BleAdapter wurde nicht erstellt.");
                adapter.OnMessageReceived += OnMessage;
                adapter.OnErrorReceived += OnError;
            }
            catch (Exception e)
            {
                requesting = false; SetStatus("Initialisierung: " + e.Message); yield break;
            }
        }
        // The upstream initializer requests Android-12 permissions even on Android 10.
        // Its brief permission Activity pauses Unity asynchronously. Keep initialization
        // protected until focus and lifecycle events have settled, then begin the scan.
        scanFocusReadyAfter = Time.realtimeSinceStartup + .5f;
        float focusDeadline = Time.realtimeSinceStartup + 90;
        SetStatus("Bluetooth wird vorbereitet. Warte auf aktive Brillen-App …");
        while (true)
        {
            if (Time.realtimeSinceStartup > focusDeadline)
            { requesting = false; SetStatus("Brillen-App nicht aktiv. Zur App zurückkehren und erneut suchen."); yield break; }
            if (applicationPaused || !Application.isFocused) scanFocusReadyAfter = Time.realtimeSinceStartup + .5f;
            else if (Time.realtimeSinceStartup >= scanFocusReadyAfter) break;
            yield return null;
        }
        string scanError = null;
        try { AndroidBleScanSupport.PrepareScan(apiLevel: AndroidBleScanSupport.ApiLevel); }
        catch (Exception e) { scanError = e.Message; }
        if (scanError != null) { requesting = false; SetStatus("BLE-Suche nicht möglich: " + scanError); yield break; }
        requesting = false;
        DeviceIds.Clear(); DeviceNames.Clear(); SelectedIndex = 0;
        scan = new DiscoverDevices(OnDeviceFound, OnScanFinished, scanSeconds * 1000);
        Scanning = true;
        // Upstream stop does not cancel its delayed timer: don't overlap scan sessions.
        scanRestartAfter = Time.realtimeSinceStartup + scanSeconds + 1;
        SetStatus("Suche BLE-Geräte …");
        try { scan.Start(); }
        catch (Exception e) { Scanning = false; SetStatus("Scan: " + e.Message); }
    }

    void OnDeviceFound(string id, string name)
    {
        if (!Scanning || string.IsNullOrEmpty(id)) return;
        // Discovery is deliberately unfiltered; profiles only describe data channels.
        int index = DeviceIds.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) { DeviceIds.Add(id); DeviceNames.Add(string.IsNullOrEmpty(name) ? "Ohne Namen" : name); }
        else if (!string.IsNullOrEmpty(name)) DeviceNames[index] = name;
        Notify();
    }
    void OnScanFinished()
    {
        if (!Scanning) return;
        Scanning = false;
        SetStatus(DeviceIds.Count > 0
            ? "Scan beendet: " + DeviceIds.Count + " Geräte. Gerät auswählen."
            : "Keine BLE-Sendesignale empfangen. Gerät in Such-/Werbemodus bringen; Bluetooth-Classic-Geräte erscheinen hier nicht.");
    }
    public void StopScan()
    {
        if (requesting) { StopAllCoroutines(); requesting = false; SetStatus("Suche abgebrochen."); }
        if (!Scanning) return;
        Scanning = false;
        try { scan?.End(); } catch (Exception e) { SetStatus(e.Message); }
        scan = null; SetStatus("Suche gestoppt.");
    }
    public void SelectDevice(int index)
    {
        if (Busy || Connected || index < 0 || index >= DeviceIds.Count) return;
        SelectedIndex = index; Notify();
    }
    public bool TryConnectDevice(string deviceId, out string error)
    {
        error = null;
        if (requesting || Busy || Connected)
        { error = "Verbindung bereits aktiv oder Initialisierung läuft. Zuerst trennen oder warten."; return false; }
        int index = DeviceIds.FindIndex(id => string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(deviceId) || index < 0)
        { error = "Gerät nicht in der aktuellen Suchliste. Erneut suchen und auswählen."; return false; }
        SelectDevice(index);
        Connect();
        if (Busy || Connected) return true;
        error = Status;
        return false;
    }
    public void Connect()
    {
        if (Busy || Connected || manager == null) return;
        if (SelectedIndex < 0 || SelectedIndex >= DeviceIds.Count) { SetStatus("Zuerst ein Gerät auswählen."); return; }
        try { Profile.Validate(); } catch (Exception e) { SetStatus("Geräteprofil: " + e.Message); return; }
        StopScan();
        ActiveProfile = Instantiate(Profile);
        EnsureOperations(); operations.Clear();
        connectedId = DeviceIds[SelectedIndex]; foundCharacteristics.Clear();
        Busy = true; gattOpen = false; connectionDeadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
        connection = new ConnectToDevice(connectedId, OnConnected, OnDisconnected, OnService, OnCharacteristic);
        SetStatus("Verbinde mit " + DeviceNames[SelectedIndex] + " …");
        try { connection.Start(); } catch (Exception e) { OnDisconnected(connectedId); SetStatus(e.Message); }
    }
    void OnService(string device, string service)
    {
        Debug.Log("[BLE] Service " + service + " auf " + device);
    }
    void OnCharacteristic(string device, string service, string characteristic)
    {
        Debug.Log("[BLE] Characteristic " + service + "/" + characteristic + " auf " + device);
        foundCharacteristics.Add(BleUuid.Key(service, characteristic));
    }
    void OnConnected(string id)
    {
        if (!Busy) return;
        Busy = false; Connected = true;
        SetStatus("Verbunden. Datenkanäle erkannt.");
        try { GattReady?.Invoke(id); }
        catch (Exception e) { OnError("Datenkanäle: " + e.Message); }
    }
    void OnDisconnected(string id)
    {
        Busy = false; Connected = false; gattOpen = false; connection = null;
        operations?.Clear(); foundCharacteristics.Clear();
        if (ActiveProfile != null) Destroy(ActiveProfile);
        ActiveProfile = null; connectedId = null; SetStatus("Verbindung getrennt.");
    }
    public void Disconnect()
    {
        StopScan();
        // Closing GATT also ends updates. Do not issue a CCCD write while an ATT read/write is pending.
        operations?.Clear();
        // Upstream only stores a GATT handle after ConnectedToGattServer. Before
        // that point disconnectDevice fails with "Can't find connected device".
        // A late callback is closed in OnMessage, even after our command is gone.
        try { if (gattOpen || Connected) connection?.Disconnect(); } catch (Exception e) { Debug.LogWarning(e.Message); }
        OnDisconnected(connectedId);
    }
    void OnMessage(BleObject message)
    {
        if (shuttingDown) return;
        if (message.Command == "ConnectedToGattServer")
        {
            if (connection == null || !string.Equals(message.Device, connectedId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[BLE] Verspätete Verbindung nach Abbruch wird geschlossen: " + message.Device);
                new ConnectToDevice(message.Device).Disconnect();
                return;
            }
            gattOpen = true;
            SetStatus("Bluetooth verbunden. Sensorkanäle werden gesucht …");
        }
        // Deliberately dispatch existing commands directly. No retained parallel commands
        // or subscription timeouts in the upstream BleManager queue.
        if (Scanning && scan != null && scan.CommandReceived(message)) { scan.End(); scan = null; }
        if (connection != null) connection.CommandReceived(message);
        if (!Connected || !string.Equals(message.Device,connectedId,StringComparison.OrdinalIgnoreCase)) return;
        if (message.HasError) { OnError(message.ErrorMessage); return; }
        try
        {
            byte[] data = string.IsNullOrEmpty(message.Base64Message) ? new byte[0] : message.GetByteMessage();
            operations?.Handle(message.Device,message.Service,message.Characteristic,message.Command,message.Descriptor,data,false,null);
            if (Connected && message.Command == "CharacteristicValueChanged")
                DataReceived?.Invoke(message.Service,message.Characteristic,data);
        }
        catch (Exception e) { OnError(e.Message); }
    }
    void OnError(string error)
    {
        if (Busy || Connected) Disconnect();
        SetStatus("BLE-Fehler: " + error); GattError?.Invoke(error);
    }
    protected virtual void Update()
    {
        if (Busy && Time.realtimeSinceStartup > connectionDeadline) { Disconnect(); SetStatus("Verbindungszeitlimit nach " + connectTimeoutSeconds + " s. Sensor neu starten; andere Sensor-Apps schließen und erneut versuchen."); }
        if (Connected) operations?.Tick(Time.realtimeSinceStartup);
        if (Scanning && Time.realtimeSinceStartup > scanRestartAfter + 2) StopScan();
    }
    protected virtual void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        scanFocusReadyAfter = Time.realtimeSinceStartup + .5f;
        if (paused && !requesting) Disconnect();
    }
    protected virtual void OnApplicationFocus(bool focused) => scanFocusReadyAfter = Time.realtimeSinceStartup + .5f;
    protected virtual void OnDisable()
    {
        StopAllCoroutines(); requesting=false; Disconnect();
        // Keep the callback until destruction so a late autoConnect can be closed.
    }
    protected virtual void OnDestroy()
    {
        shuttingDown = true;
        if (adapter != null) { adapter.OnMessageReceived -= OnMessage; adapter.OnErrorReceived -= OnError; }
        Disconnect(); operations?.Dispose();
        if (fallbackProfile != null) Destroy(fallbackProfile);
        Changed = null; GattReady = null; GattError = null; DataReceived = null; OperationCompleted = null;
        // BleManager and its adapter are scene objects and are cleaned up by Unity.
    }
}
