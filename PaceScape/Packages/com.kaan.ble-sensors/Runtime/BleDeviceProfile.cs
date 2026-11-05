using System;
using System.Collections.Generic;
using UnityEngine;

public enum BleValueFormat { HeartRate, Temperature, PulseOximeter, Battery, Text, CosinussStatus, RawHex, FtmsTreadmillData }
public enum BleUpdateMode { NotifyPreferred = 1, IndicatePreferred = 2 }

[Serializable]
public sealed class BleChannel
{
    public string id = "value";
    public string label = "Messwert";
    public string serviceUuid = "180f";
    public string characteristicUuid = "2a19";
    public BleValueFormat format = BleValueFormat.RawHex;
    public bool read = true;
    public bool subscribe;
    public BleUpdateMode updateMode = BleUpdateMode.NotifyPreferred;
    [Min(1)] public float staleSeconds = 15;
    public string Service => BleUuid.Normalize(serviceUuid);
    public string Characteristic => BleUuid.Normalize(characteristicUuid);
}

public static class BleUuid
{
    public static string Normalize(string value)
    {
        string s = (value ?? "").Trim();
        if (s.Length == 4) s = "0000" + s + "-0000-1000-8000-00805f9b34fb";
        if (!Guid.TryParseExact(s, "D", out var uuid)) throw new FormatException("Ungültige BLE-UUID: " + value);
        return uuid.ToString("D");
    }
    public static string Key(string service, string characteristic) => Normalize(service) + "/" + Normalize(characteristic);
}

[CreateAssetMenu(menuName = "BLE/Geräteprofil", fileName = "BleDeviceProfile")]
public sealed class BleDeviceProfile : ScriptableObject
{
    [Tooltip("Leere Liste zeigt alle Namen, einschließlich namenloser Geräte. Teilstrings, Groß-/Kleinschreibung egal.")]
    public string[] nameContains = new string[0];
    [Tooltip("Optional: exakte Gerätekennung aus dem Scan. Bei wechselnden BLE-Adressen leer lassen.")]
    public string deviceId = "";
    public List<BleChannel> channels = new List<BleChannel>();

    public bool Matches(string id, string name)
    {
        if (!string.IsNullOrWhiteSpace(deviceId) && !string.Equals(deviceId.Trim(), id, StringComparison.OrdinalIgnoreCase)) return false;
        bool hasFilter = false;
        foreach (string part in nameContains ?? new string[0])
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            hasFilter = true;
            if ((name ?? "").IndexOf(part.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return !hasFilter;
    }

    public void Validate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in channels ?? throw new FormatException("Kanalliste fehlt."))
        {
            if (c == null || string.IsNullOrWhiteSpace(c.id) || !ids.Add(c.id)) throw new FormatException("Kanal-IDs müssen ausgefüllt und eindeutig sein.");
            if (!keys.Add(BleUuid.Key(c.serviceUuid, c.characteristicUuid))) throw new FormatException("Doppelter Datenkanal: " + c.label);
            if (!c.read && !c.subscribe) throw new FormatException("Read oder Subscribe aktivieren: " + c.label);
            if (c.staleSeconds <= 0) throw new FormatException("Ungültige Aktualitätsdauer: " + c.label);
        }
    }
}
