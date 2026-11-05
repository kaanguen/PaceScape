using System;

public static class HeartRateParser
{
    public sealed class Reading
    {
        public int Bpm;
        public bool? ContactDetected;
        public int? EnergyKiloJoules;
        public double[] RrMilliseconds;
    }

    // Kept for callers that only need bpm.
    public static int Parse(byte[] packet) { return ParseMeasurement(packet).Bpm; }

    // Bluetooth SIG Heart Rate Measurement (2A37), little endian.
    public static Reading ParseMeasurement(byte[] packet)
    {

    
        if (packet == null || packet.Length < 2)
            throw new FormatException("Herzfrequenz-Paket zu kurz.");
        int flags = packet[0];
        if ((flags & 0xE0) != 0)
            throw new FormatException("Unbekannte Herzfrequenz-Flags.");


        bool sixteenBit = (flags & 1) != 0;
        int offset = sixteenBit ? 3 : 2;
        if (packet.Length < offset)
            throw new FormatException("16-Bit-Herzfrequenz ist unvollständig.");
        var result = new Reading
        {
            Bpm = sixteenBit ? packet[1] | (packet[2] << 8) : packet[1],
            ContactDetected = (flags & 4) != 0 ? (bool?)((flags & 2) != 0) : null
        };
        if ((flags & 8) != 0)
        {
            if (packet.Length < offset + 2)
                throw new FormatException("Energy-Expended-Feld ist unvollständig.");
            result.EnergyKiloJoules = packet[offset] | (packet[offset + 1] << 8);
            offset += 2;
        }

        int remaining = packet.Length - offset;
        if ((flags & 16) != 0)
        {
            if (remaining < 2 || remaining % 2 != 0)
                throw new FormatException("RR-Intervalle sind unvollständig.");
            result.RrMilliseconds = new double[remaining / 2];
            for (int i = 0; i < result.RrMilliseconds.Length; i++)
            {
                int rr = packet[offset] | (packet[offset + 1] << 8);
                // Unit on the wire: 1/1024 second, NOT plain milliseconds.
                result.RrMilliseconds[i] = rr * 1000.0 / 1024.0;
                offset += 2;
            }
        }
        else
        {
            if (remaining != 0)
                throw new FormatException("Unerwartete Bytes im Herzfrequenz-Paket.");
            result.RrMilliseconds = new double[0];
        }

        if (result.ContactDetected == false)
            throw new FormatException("Sensor meldet keinen Hautkontakt.");
        return result;
    }
}
