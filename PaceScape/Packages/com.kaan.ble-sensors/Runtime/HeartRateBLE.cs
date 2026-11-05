using UnityEngine;

// Compatibility component: keep the original script GUID and serialized references.
// New projects can use BleConnection directly with any BleDeviceProfile.
public sealed class HeartRateBLE : BleConnection
{
    [HideInInspector] public string serviceUuid = "180d";
    [HideInInspector] public string characteristicUuid = "2a37";
    protected override BleDeviceProfile CreateFallbackProfile() => CosinussProfileDefaults.Create(serviceUuid, characteristicUuid);
}
