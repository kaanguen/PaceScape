using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Profile-driven interpretation; no Android calls or plugin commands.
public class SensorMeasurements : MonoBehaviour
{
    public enum OxygenFormat { Auto, Standard11073, LegacyIeeeHalf }
    public BleConnection bluetooth;
    public OxygenFormat oxygenFormat = OxygenFormat.Auto;
    public event Action Changed;
    public event Action<double> TreadmillSpeedReceived;
    public event Action<uint> TreadmillDistanceReceived;
    private double treadmillDistanceAt = double.NegativeInfinity;
    private uint treadmillTotalDistance;
    public bool TryGetTreadmillTotalDistanceMetres(out uint metres)
    {
        metres = treadmillTotalDistance;
        return isActiveAndEnabled && bluetooth != null && bluetooth.Connected &&
            Time.realtimeSinceStartupAsDouble - treadmillDistanceAt <= 2;
    }
    public event Action TreadmillSignalLost;
    sealed class Value {
        public BleChannel Channel;
        public string Text = "wird eingerichtet";
        public bool Received, Waiting;
        public float At;
        public double OxygenPercent = double.NaN;
        public double HeartRateBpm = double.NaN, TemperatureCelsius = double.NaN;
        public double PulseBpm = double.NaN, PerfusionPercent = double.NaN;
        public double TreadmillSpeedKmh = double.NaN;
        public bool MaximumQuality;
        public string Quality="—", Hint="noch kein Hinweis empfangen";
        public float HintAt=-1;
    }
    readonly Dictionary<string,Value> byChannel = new Dictionary<string,Value>();
    readonly List<Value> ordered = new List<Value>();
    string address="", model="", firmware="";
    Value heartRate;
    public string Measurement => heartRate == null ? "— bpm" : heartRate.Text;
    public string RrIntervals { get; private set; } = "";
    public bool TryGetHeartRateBpm(out double value) => TryGetFreshNumber(BleValueFormat.HeartRate, v => v.HeartRateBpm, out value);
    public bool TryGetTemperatureCelsius(out double value) => TryGetFreshNumber(BleValueFormat.Temperature, v => v.TemperatureCelsius, out value);
    public bool TryGetPulseBpm(out double value) => TryGetFreshNumber(BleValueFormat.PulseOximeter, v => v.PulseBpm, out value);
    public bool TryGetPerfusionPercent(out double value) => TryGetFreshNumber(BleValueFormat.PulseOximeter, v => v.PerfusionPercent, out value);
    public bool TryGetTreadmillSpeedKmh(out double value) => TryGetFreshNumber(BleValueFormat.FtmsTreadmillData, v => v.TreadmillSpeedKmh, out value);
    public bool HasTreadmillData => bluetooth != null && bluetooth.Connected &&
        bluetooth.HasCharacteristic(FtmsTreadmillData.ServiceUuid, FtmsTreadmillData.CharacteristicUuid);
    public string TreadmillStatus
    {
        get
        {
            if (bluetooth == null || !bluetooth.Connected) return "Kein Laufband verbunden.";
            if (!HasTreadmillData) return "Verbundenes Gerät bietet keinen FTMS-Laufbandkanal (1826/2ACD).";
            foreach (var value in ordered)
                if (value.Channel.format == BleValueFormat.FtmsTreadmillData) return value.Text;
            return "FTMS erkannt. Geschwindigkeitskanal wird eingerichtet.";
        }
    }

    private bool TryGetFreshNumber(BleValueFormat format, Func<Value, double> read, out double number)
    {
        number = double.NaN;
        if (!isActiveAndEnabled || bluetooth == null || !bluetooth.Connected) return false;
        foreach (var value in ordered)
        {
            if (value.Channel.format != format || !value.Received ||
                Time.realtimeSinceStartup - value.At > value.Channel.staleSeconds) continue;
            double candidate = read(value);
            if (double.IsNaN(candidate) || double.IsInfinity(candidate)) continue;
            number = candidate;
            return true;
        }
        return false;
    }
    // Numeric data for gameplay/telemetry; never parse the localized UI text.
    public bool TryGetOxygenPercent(out double percent) {
        percent=double.NaN;
        if (!isActiveAndEnabled || bluetooth==null || !bluetooth.Connected) return false;
        foreach (var v in ordered) {
            if (v.Channel.format!=BleValueFormat.PulseOximeter || !v.Received ||
                Time.realtimeSinceStartup-v.At>v.Channel.staleSeconds || double.IsNaN(v.OxygenPercent)) continue;
            percent=v.OxygenPercent; return true;
        }
        return false;
    }
    public string Details {
        get {
            string temperature="—", spo2="—";
            bool hasTemperature=false, hasSpo2=false;
            foreach (var v in ordered) {
                if (!hasTemperature && v.Channel.format==BleValueFormat.Temperature) {
                    temperature=v.Text;
                    hasTemperature=true;
                } else if (!hasSpo2 && v.Channel.format==BleValueFormat.PulseOximeter) {
                    int perfusion=v.Text.IndexOf(" | Perfusion:",StringComparison.Ordinal);
                    spo2=perfusion<0 ? v.Text : v.Text.Substring(0,perfusion);
                    if (v.Text.Contains("(Status prüfen)")) spo2+=" · Status prüfen";
                    hasSpo2=true;
                }
            }
            return "<size=13><color=#E3DEF7><b>TEMPERATUR</b></color></size>\n"
                +"<size=28><color=#FFFFFF>"+temperature+"</color></size>\n\n"
                +"<size=13><color=#E3DEF7><b>SpO2</b></color></size>\n"
                +"<size=28><color=#FFFFFF>"+spo2+"</color></size>";
        }
    }
    protected virtual void OnEnable() {
        bluetooth = bluetooth != null ? bluetooth : GetComponent<BleConnection>();
        if (bluetooth==null) { Debug.LogError("SensorMeasurements: BleConnection fehlt."); enabled=false; return; }
        bluetooth.GattReady+=Configure; bluetooth.DataReceived+=OnData;
        bluetooth.OperationCompleted+=OnOperation; bluetooth.Changed+=OnState;
        if (bluetooth.Connected) Configure(bluetooth.ConnectedDeviceId);
    }
    protected virtual void OnDisable() {
        if (bluetooth!=null) {
            bluetooth.GattReady-=Configure; bluetooth.DataReceived-=OnData;
            bluetooth.OperationCompleted-=OnOperation; bluetooth.Changed-=OnState;
            if (bluetooth.Connected && !string.IsNullOrEmpty(address)) bluetooth.Disconnect();
        }
        Clear();
    }
    void Clear() {
        byChannel.Clear(); ordered.Clear(); heartRate=null;
        address=""; model=""; firmware=""; RrIntervals=""; treadmillDistanceAt=double.NegativeInfinity; TreadmillSignalLost?.Invoke(); Changed?.Invoke();
    }
    void OnState() { if (!bluetooth.Connected && !string.IsNullOrEmpty(address)) Clear(); }
    void Configure(string device) {
        Clear(); address=device;
        if (HasTreadmillData) TreadmillProfileDefaults.EnsureSpeedChannel(bluetooth.ActiveProfile);
        // A connection uses a session copy: edits to the Inspector asset take effect next time.
        foreach (var channel in bluetooth.ActiveProfile.channels) {
            var v=new Value { Channel=channel };
            ordered.Add(v); byChannel.Add(BleUuid.Key(channel.Service,channel.Characteristic),v);
            if (heartRate==null && channel.format==BleValueFormat.HeartRate) heartRate=v;
            if (!bluetooth.HasCharacteristic(channel.Service,channel.Characteristic)) { v.Text="nicht angeboten"; continue; }
            if (channel.read) bluetooth.Read(channel.Service,channel.Characteristic);
            if (channel.subscribe) bluetooth.Subscribe(channel.Service,channel.Characteristic,channel.updateMode);
        }
        Changed?.Invoke();
    }
    void OnOperation(string s,string c,bool subscribe,string error) {
        if (!byChannel.TryGetValue(BleUuid.Key(s,c),out var v)) return;
        if (error!=null)
        {
            if (!v.Received) v.Text=error;
            if (v.Channel.format == BleValueFormat.FtmsTreadmillData)
            { v.Received = false; treadmillDistanceAt=double.NegativeInfinity; TreadmillSignalLost?.Invoke(); }
        }
        else if (subscribe && !v.Received) { v.Text="wartet auf Daten"; v.Waiting=true; v.At=Time.realtimeSinceStartup; }
        Changed?.Invoke();
    }
    void OnData(string s,string c,byte[] bytes) {
        if (!byChannel.TryGetValue(BleUuid.Key(s,c),out var v)) return;
        try {
            if (v.Channel.format == BleValueFormat.FtmsTreadmillData)
            {
                FtmsTreadmillData.Read(bytes, out double? speed, out uint? distance);
                if (distance.HasValue)
                {
                    treadmillTotalDistance = distance.Value;
                    treadmillDistanceAt = Time.realtimeSinceStartupAsDouble;
                    TreadmillDistanceReceived?.Invoke(distance.Value);
                }
                if (speed.HasValue)
                {
                    v.TreadmillSpeedKmh = speed.Value;
                    v.Text = Number(speed.Value, " km/h");
                    v.Received = true; v.Waiting = false; v.At = Time.realtimeSinceStartup;
                    TreadmillSpeedReceived?.Invoke(speed.Value);
                }
                Changed?.Invoke();
                return; // Distance-only packets never renew the speed timestamp.
            }
            string text=Decode(v,bytes);
            if (text==null) return;
            v.Text=text; v.Received=true; v.Waiting=false; v.At=Time.realtimeSinceStartup;
            if (v==heartRate) bluetooth.ShowStatus("Herzfrequenz empfangen.");
        } catch (FormatException e) {
            v.Text="Ungültige Daten: "+e.Message; v.Received=false; v.Waiting=false;
            if (v==heartRate) RrIntervals="";
            if (v.Channel.format == BleValueFormat.FtmsTreadmillData) { treadmillDistanceAt=double.NegativeInfinity; TreadmillSignalLost?.Invoke(); }
            Debug.LogWarning("[Sensor] "+v.Channel.label+": "+e.Message);
        }
        Changed?.Invoke();
    }
    string Decode(Value value,byte[] bytes) {
        var channel=value.Channel;
        switch (channel.format) {
            case BleValueFormat.HeartRate:
                var hr=HeartRateParser.ParseMeasurement(bytes);
                value.HeartRateBpm = hr.Bpm > 0 ? hr.Bpm : double.NaN;
                var rr=new List<string>();
                for (int i=0;i<Math.Min(3,hr.RrMilliseconds.Length);i++) rr.Add(hr.RrMilliseconds[i].ToString("0.#",CultureInfo.InvariantCulture));
                if (heartRate!=null && channel==heartRate.Channel)
                    RrIntervals=rr.Count==0 ? "" : string.Join(" / ",rr)+" ms"+(hr.RrMilliseconds.Length>3 ? " …" : "");
                return hr.Bpm+" bpm";
            case BleValueFormat.Temperature:
                var t=CosinussParsers.Temperature(bytes);
                value.TemperatureCelsius = t.Special == null ? t.Celsius : double.NaN;
                return Number(t.Celsius," °C")+(t.Special==null ? "" : " ("+t.Special+")");
            case BleValueFormat.PulseOximeter:
                var p=CosinussParsers.Plx(bytes,Encoding());
                bool warning=(p.MeasurementStatus.GetValueOrDefault() & 0xFC00)!=0 || (p.DeviceStatus.GetValueOrDefault() & 0xFFFE)!=0;
                value.PulseBpm = !warning && p.Pulse > 0 ? p.Pulse : double.NaN;
                value.PerfusionPercent = !warning && p.Perfusion >= 0 ? p.Perfusion : double.NaN;
                value.OxygenPercent=!warning && !double.IsNaN(p.Spo2) && !double.IsInfinity(p.Spo2) && p.Spo2>0 && p.Spo2<=100
                    ? p.Spo2 : double.NaN;
                return (p.Spo2<0 || p.Spo2>100 ? "ungültiger Wertebereich" : Number(p.Spo2," %"))
                    +" | Perfusion: "+Number(p.Perfusion," %")+(warning ? " (Status prüfen)" : "")
                    +(p.MeasurementStatus.HasValue ? " M=0x"+p.MeasurementStatus.Value.ToString("X4") : "")
                    +(p.DeviceStatus.HasValue ? " D=0x"+p.DeviceStatus.Value.ToString("X6") : "");
            case BleValueFormat.Battery: return CosinussParsers.Battery(bytes)+" %";
            case BleValueFormat.Text:
                string text=System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0').Replace('\n',' ').Replace('\r',' ');
                if (channel.id=="model") model=text;
                if (channel.id=="firmware") firmware=text;
                return text;
            case BleValueFormat.CosinussStatus:
                var s=CosinussParsers.Sensor(bytes);
                if (s.ErrorCode.HasValue) {
                    value.Hint="0x"+s.ErrorCode.Value.ToString("X2")+": "+CosinussParsers.ErrorDescription(s.ErrorCode.Value);
                    value.HintAt=Time.realtimeSinceStartup;
                    return VendorText(value);
                }
                if (s.Quality.HasValue && (s.IsMaximumQuality || !value.MaximumQuality)) {
                    value.MaximumQuality=s.IsMaximumQuality;
                    value.Quality=s.Quality.Value.ToString()+(s.IsMaximumQuality ? " (max)" : "");
                    return VendorText(value);
                }
                return null;
            case BleValueFormat.RawHex:
                if (bytes.Length==0) return "(leeres Paket)";
                return BitConverter.ToString(bytes,0,Math.Min(bytes.Length,64)).Replace('-',' ')+(bytes.Length>64 ? " … ("+bytes.Length+" Bytes)" : "");
            default: throw new FormatException("Unbekanntes Datenformat.");
        }
    }
    static string VendorText(Value value) => value.Quality+"\nSensorhinweis: "+value.Hint;
    static string Number(double x,string unit) => double.IsNaN(x) || double.IsInfinity(x) ? "nicht verfügbar" : x.ToString("0.##",CultureInfo.InvariantCulture)+unit;
    CosinussParsers.PlxEncoding Encoding() {
        if (oxygenFormat==OxygenFormat.LegacyIeeeHalf) return CosinussParsers.PlxEncoding.LegacyIeeeHalf;
        if (oxygenFormat==OxygenFormat.Auto && model.IndexOf("two",StringComparison.OrdinalIgnoreCase)>=0
            && Version.TryParse(firmware.TrimStart('v','V').Split('-')[0],out var v) && v<=new Version(2,0,2)) return CosinussParsers.PlxEncoding.LegacyIeeeHalf;
        return CosinussParsers.PlxEncoding.Standard11073;
    }
    string EncodingLabel() => Encoding()==CosinussParsers.PlxEncoding.LegacyIeeeHalf ? "IEEE Half (Legacy)"
        : oxygenFormat==OxygenFormat.Auto ? "IEEE 11073 (Standardannahme)" : "IEEE 11073";
    protected virtual void Update() {
        bool changed=false; float now=Time.realtimeSinceStartup;
        foreach (var v in ordered) {
            if (v.Channel.format==BleValueFormat.CosinussStatus && v.HintAt>=0 && now-v.HintAt>15) {
                v.HintAt=-1; v.Hint="— (kein aktueller Hinweis)";
                if (v.Received) v.Text=VendorText(v);
                changed=true;
            }
            if (v.Waiting && now-v.At>(v==heartRate ? 25 : 15)) { v.Waiting=false; v.Text="keine Updates empfangen"; changed=true; }
            if (v.Received && v.Channel.format!=BleValueFormat.Text && now-v.At>v.Channel.staleSeconds) {
                v.Received=false; v.Text="— (keine aktuellen Daten)"; changed=true;
                if (v==heartRate) RrIntervals="";
            }
        }
        if (changed) Changed?.Invoke();
    }
}
