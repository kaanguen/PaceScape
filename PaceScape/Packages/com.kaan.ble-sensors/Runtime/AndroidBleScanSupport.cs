using System;
using UnityEngine;

// Version-bound access to the existing Velorexe scanner; no name/service filters.
internal static class AndroidBleScanSupport
{
    internal static int ApiLevel
    {
        get
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                return version.GetStatic<int>("SDK_INT");
        }
    }

    internal static void PrepareScan(int apiLevel)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var bluetooth = new AndroidJavaClass("android.bluetooth.BluetoothAdapter"))
        using (var adapter = bluetooth.CallStatic<AndroidJavaObject>("getDefaultAdapter"))
        {
            if (adapter == null || !adapter.Call<bool>("isEnabled"))
                throw new InvalidOperationException("Bluetooth auf der Brille einschalten.");
        }
        if (apiLevel <= 30)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var location = activity.Call<AndroidJavaObject>("getSystemService", "location"))
                if (apiLevel >= 28 && !location.Call<bool>("isLocationEnabled"))
                    throw new InvalidOperationException("Standortdienste auf der Brille einschalten; Android benötigt sie für BLE-Suchen.");
        }
        // The plugin suppresses callbacks for addresses already in its map. Clear
        // it while the scanner is stopped, otherwise repeat scans appear empty.
        using (var plugin = new AndroidJavaClass("com.velorexe.unityandroidble.UnityAndroidBLE"))
        using (var devices = plugin.GetStatic<AndroidJavaObject>("mLeDeviceListAdapter"))
        using (var map = devices.Get<AndroidJavaObject>("mLeDevicesMap"))
            map.Call("clear");
#else
        throw new PlatformNotSupportedException("BLE-Suche benötigt einen Android-Gerätebuild.");
#endif
    }
}
