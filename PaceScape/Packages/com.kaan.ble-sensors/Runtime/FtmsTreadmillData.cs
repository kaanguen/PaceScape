// Decodes speed and optional total distance from FTMS Treadmill Data packets.

using System;

// Reads instantaneous speed and optional total distance from FTMS Treadmill Data.
public static class FtmsTreadmillData
{
    public const string ServiceUuid = "1826";
    public const string CharacteristicUuid = "2acd";

    // Reads optional fields according to the packet flags and validates field lengths.
    public static void Read(byte[] packet, out double? speedKmh, out uint? totalDistanceMetres)
    {
        speedKmh = null;
        totalDistanceMetres = null;
        if (packet == null || packet.Length < 2) throw new FormatException("FTMS: Flags fehlen.");
        int flags = packet[0] | packet[1] << 8;
        int offset = 2;
        // More Data = 1 omits speed, but optional distance can still be present.
        if ((flags & 1) == 0)
        {
            Require(packet, offset, 2);
            speedKmh = (packet[offset] | packet[offset + 1] << 8) / 100.0;
            offset += 2;
        }
        if ((flags & 2) != 0) { Require(packet, offset, 2); offset += 2; } // Average speed
        if ((flags & 4) != 0)
        {
            Require(packet, offset, 3);
            totalDistanceMetres = (uint)(packet[offset] | packet[offset + 1] << 8 | packet[offset + 2] << 16);
        }
    }

    public static bool TryReadSpeedKmh(byte[] packet, out double speedKmh)
    {
        Read(packet, out double? speed, out _);
        speedKmh = speed ?? double.NaN;
        return speed.HasValue;
    }

    private static void Require(byte[] packet, int offset, int count)
    {
        if (packet.Length - offset < count) throw new FormatException("FTMS: Geschwindigkeits-/Distanzfeld unvollständig.");
    }
}
