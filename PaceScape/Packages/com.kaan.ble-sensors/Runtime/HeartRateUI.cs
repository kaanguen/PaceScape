using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class HeartRateUI : MonoBehaviour
{
    public BleConnection bluetooth;
    public TMP_Dropdown devices;
    public TMP_Text statusText;
    public TMP_Text heartRateText;
    public TMP_Text sensorDetailsText;
    public SensorMeasurements sensor;
    public UnityEngine.UI.Button scanButton;
    public UnityEngine.UI.Button stopButton;
    public UnityEngine.UI.Button connectButton;
    public UnityEngine.UI.Button disconnectButton;

    readonly List<string> displayedDevices = new List<string>();

    void OnEnable()
    {
        if (bluetooth == null || devices == null || statusText == null || heartRateText == null
            || scanButton == null || stopButton == null || connectButton == null || disconnectButton == null)
        {
            Debug.LogError("HeartRateUI: Bitte alle Inspector-Felder zuweisen.");
            enabled = false; return;
        }
        sensor = sensor != null ? sensor : GetComponent<SensorMeasurements>();
        if (sensor == null) sensor = gameObject.AddComponent<SensorMeasurements>();
        if (sensorDetailsText != null)
        {
            sensorDetailsText.richText = true;
            sensorDetailsText.enableAutoSizing = false;
            sensorDetailsText.fontSize = 28;
            sensorDetailsText.fontSizeMin = 20;
            sensorDetailsText.fontSizeMax = 24;
            sensorDetailsText.alignment = TextAlignmentOptions.MidlineLeft;
        }
        heartRateText.richText = true;
        heartRateText.fontSize = 28;
        heartRateText.alignment = TextAlignmentOptions.MidlineLeft;
        var heartRateLayout=heartRateText.GetComponent<UnityEngine.UI.LayoutElement>();
        if (heartRateLayout != null)
        {
            heartRateLayout.minHeight=86;
            heartRateLayout.preferredHeight=86;
        }
        var title=transform.Find("Panel/Title");
        if (title != null)
        {
            var titleText=title.GetComponent<TMP_Text>();
            if (titleText != null)
            {
                titleText.text="VITALWERTE";
                titleText.fontSize=13;
            }
        }
        sensor.Changed += Refresh;
        bluetooth.Changed += Refresh;
        devices.onValueChanged.AddListener(bluetooth.SelectDevice);
        scanButton.onClick.AddListener(bluetooth.StartScan);
        stopButton.onClick.AddListener(bluetooth.StopScan);
        connectButton.onClick.AddListener(bluetooth.Connect);
        disconnectButton.onClick.AddListener(bluetooth.Disconnect);
        Refresh();
    }
    void OnDisable()
    {
        if (bluetooth == null) return;
        bluetooth.Changed -= Refresh;
        if (sensor != null) sensor.Changed -= Refresh;
        if (devices != null) devices.onValueChanged.RemoveListener(bluetooth.SelectDevice);
        if (scanButton != null) scanButton.onClick.RemoveListener(bluetooth.StartScan);
        if (stopButton != null) stopButton.onClick.RemoveListener(bluetooth.StopScan);
        if (connectButton != null) connectButton.onClick.RemoveListener(bluetooth.Connect);
        if (disconnectButton != null) disconnectButton.onClick.RemoveListener(bluetooth.Disconnect);
    }
    void Refresh()
    {
        statusText.text = bluetooth.Status;
        heartRateText.text = "<size=13><color=#E3DEF7><b>HERZFREQUENZ</b></color></size>\n"
            +sensor.Measurement;
        if (sensorDetailsText != null && sensor != null) sensorDetailsText.text = sensor.Details;
        var names = new List<string>();
        for (int i = 0; i < bluetooth.DeviceIds.Count; i++)
            names.Add(bluetooth.DeviceNames[i] + " | " + bluetooth.DeviceIds[i]);
        bool listChanged = names.Count != displayedDevices.Count;
        for (int i = 0; !listChanged && i < names.Count; i++)
            listChanged = names[i] != displayedDevices[i];
        // Keep an open dropdown intact while scan/status updates arrive.
        if (listChanged || devices.options.Count != names.Count)
        {
            devices.ClearOptions();
            devices.AddOptions(names);
            displayedDevices.Clear();
            displayedDevices.AddRange(names);
        }
        devices.SetValueWithoutNotify(bluetooth.SelectedIndex);
        devices.RefreshShownValue();
        devices.interactable = names.Count > 0 && !bluetooth.Busy && !bluetooth.Connected;
        scanButton.interactable = !bluetooth.Scanning && !bluetooth.Busy && !bluetooth.Connected;
        stopButton.interactable = bluetooth.Scanning;
        connectButton.interactable = names.Count > 0 && !bluetooth.Busy && !bluetooth.Connected;
        disconnectButton.interactable = bluetooth.Connected || bluetooth.Busy;
    }
}
