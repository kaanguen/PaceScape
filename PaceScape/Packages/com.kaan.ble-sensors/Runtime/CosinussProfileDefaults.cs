using UnityEngine;

// Preset data only. The BLE transport has no knowledge of these UUIDs or names.
public static class CosinussProfileDefaults
{
    public static BleDeviceProfile Create(string hrService = "180d", string hrCharacteristic = "2a37")
    {
        var p = ScriptableObject.CreateInstance<BleDeviceProfile>();
        p.name = "Cosinuss (Standardkanäle prüfen)";
        Add(p,"model","Modell","180a","2a24",BleValueFormat.Text,true,false);
        Add(p,"firmware","Firmware","180a","2a26",BleValueFormat.Text,true,false);
        Add(p,"manufacturer","Hersteller","180a","2a29",BleValueFormat.Text,true,false);
        Add(p,"software","Software","180a","2a28",BleValueFormat.Text,true,false);
        Add(p,"hardware","Hardware","180a","2a27",BleValueFormat.Text,true,false);
        Add(p,"heartRate","Herzfrequenz",hrService,hrCharacteristic,BleValueFormat.HeartRate,false,true,10);
        Add(p,"temperature","Temperatur","1809","2a1c",BleValueFormat.Temperature,false,true,35,BleUpdateMode.IndicatePreferred);
        Add(p,"spo2","SpO2","1822","2a5f",BleValueFormat.PulseOximeter,false,true);
        Add(p,"battery","Akku","180f","2a19",BleValueFormat.Battery,true,true,300);
        Add(p,"quality","Signalqualität","0000a000-1212-efde-1523-785feabcd123","0000a002-1212-efde-1523-785feabcd123",BleValueFormat.CosinussStatus,false,true);
        return p;
    }
    static void Add(BleDeviceProfile p,string id,string label,string s,string c,BleValueFormat format,bool read,bool subscribe,float stale=15,BleUpdateMode mode=BleUpdateMode.NotifyPreferred)
    {
        p.channels.Add(new BleChannel { id=id,label=label,serviceUuid=s,characteristicUuid=c,format=format,read=read,subscribe=subscribe,staleSeconds=stale,updateMode=mode });
    }
}
