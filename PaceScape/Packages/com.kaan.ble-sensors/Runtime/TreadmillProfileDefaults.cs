// Supplies the default FTMS profile and its treadmill speed subscription.

using UnityEngine;

public static class TreadmillProfileDefaults
{
    public static BleChannel CreateSpeedChannel() => new BleChannel {
        id = "ftmsTreadmillSpeed", label = "Laufbandgeschwindigkeit (FTMS)",
        serviceUuid = FtmsTreadmillData.ServiceUuid, characteristicUuid = FtmsTreadmillData.CharacteristicUuid,
        format = BleValueFormat.FtmsTreadmillData, read = false, subscribe = true, staleSeconds = 2
    };

    public static BleDeviceProfile Create()
    {
        var profile = ScriptableObject.CreateInstance<BleDeviceProfile>();
        profile.name = "Laufband FTMS";
        profile.channels.Add(CreateSpeedChannel());
        return profile;
    }

    // Adds the FTMS treadmill data subscription when the profile lacks it.
    public static void EnsureSpeedChannel(BleDeviceProfile profile)
    {
        string key = BleUuid.Key(FtmsTreadmillData.ServiceUuid, FtmsTreadmillData.CharacteristicUuid);
        foreach (var channel in profile.channels)
            if (BleUuid.Key(channel.Service, channel.Characteristic) == key)
            {
                channel.format = BleValueFormat.FtmsTreadmillData;
                channel.read = false; channel.subscribe = true; channel.staleSeconds = 2;
                return;
            }
        profile.channels.Add(CreateSpeedChannel());
    }
}
