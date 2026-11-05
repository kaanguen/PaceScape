using System;
using UnityEngine;

// All access to the version-bound companion JAR is isolated here.
public sealed class AndroidBleGattBackend : IBleGattBackend
{
    AndroidJavaClass bridge;
    AndroidJavaClass Bridge
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return bridge ?? (bridge = new AndroidJavaClass("com.markerml2.ble.CosinussGatt"));
#else
            throw new PlatformNotSupportedException("GATT benötigt einen Android-Gerätebuild.");
#endif
        }
    }
    public int Properties(string a,string s,string c) => Bridge.CallStatic<int>("properties",a,s,c);
    public bool Read(string a,string s,string c) => Bridge.CallStatic<bool>("read",a,s,c);
    public void Subscribe(string a,string s,string c,int mode) { Bridge.CallStatic<int>("subscribe",a,s,c,mode); }
    public void Dispose() { bridge?.Dispose(); bridge=null; }
}
