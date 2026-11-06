using System;

// Pure C#: SIG standard payloads and the public cosinuss A002 packet formats.
public static class CosinussParsers
{
    public enum PlxEncoding { Standard11073, LegacyIeeeHalf }
    public sealed class TemperatureReading {
        public double Celsius; public string Special; public byte? Type; public string Timestamp;
    }
    public sealed class PlxReading {
        public double Spo2, Pulse, Perfusion = double.NaN;
        public double FastSpo2 = double.NaN, FastPulse = double.NaN, SlowSpo2 = double.NaN, SlowPulse = double.NaN;
        public ushort? MeasurementStatus; public int? DeviceStatus;
    }
    public sealed class SensorPacket { public int? Quality; public bool IsMaximumQuality; public byte? ErrorCode; }
    sealed class Reader {
        readonly byte[] data; public int Position;
        public int Remaining => data.Length - Position;
        public Reader(byte[] bytes) { data = bytes ?? throw new FormatException("Paket fehlt"); }
        public int U8() { if (Remaining < 1) throw new FormatException("Paket zu kurz"); return data[Position++]; }
        public int U16() { return U8() | (U8() << 8); }
        public int U24() { return U16() | (U8() << 16); }
        public void End() { if (Remaining != 0) throw new FormatException("Unerwartete zusätzliche Bytes"); }
    }
    static int Signed(int n,int bits) { int sign=1 << (bits-1); return (n ^ sign)-sign; }
    public static double Float11073(uint raw,out string special) {
        int m=(int)(raw & 0xFFFFFF); special=null;
        switch(m) {
            case 0x7FFFFE: special="+Infinity"; return double.PositiveInfinity;
            case 0x800002: special="-Infinity"; return double.NegativeInfinity;
            case 0x7FFFFF: special="NaN"; return double.NaN;
            case 0x800000: special="NRes"; return double.NaN;
            case 0x800001: special="Reserviert"; return double.NaN;
        }
        return Signed(m,24)*Math.Pow(10,Signed((int)(raw >> 24),8));
    }
    public static double ShortFloat(ushort raw) {
        int m=raw & 0xFFF;
        switch(m) {
            case 0x7FE:return double.PositiveInfinity;
            case 0x802:return double.NegativeInfinity;
            case 0x7FF:case 0x800:case 0x801:return double.NaN;
        }
        return Signed(m,12)*Math.Pow(10,Signed(raw >> 12,4));
    }
    public static double HalfFloat(ushort raw) {
        double sign=(raw & 0x8000) == 0 ? 1 : -1;
        int exponent=(raw >> 10) & 31, fraction=raw & 1023;
        if (exponent == 31) return fraction == 0 ? sign*double.PositiveInfinity : double.NaN;
        if (exponent == 0) return sign*fraction*Math.Pow(2,-24);
        return sign*(1+fraction/1024.0)*Math.Pow(2,exponent-15);
    }
    public static TemperatureReading Temperature(byte[] bytes) {
        var r=new Reader(bytes); int flags=r.U8();
        if ((flags & 0xF8) != 0) throw new FormatException("Unbekannte Temperatur-Flags");
        uint raw=(uint)r.U24() | ((uint)r.U8() << 24);
        double t=Float11073(raw,out string special);
        var v=new TemperatureReading { Celsius=(flags & 1) != 0 ? (t-32)*5/9 : t,Special=special };
        if ((flags & 2) != 0) {
            int y=r.U16(),m=r.U8(),d=r.U8(),h=r.U8(),min=r.U8(),sec=r.U8();
            if (m<1 || m>12 || d<1 || d>31 || h>23 || min>59 || sec>59 || (y!=0 && (y<1582 || y>9999))) throw new FormatException("Ungültiger Zeitstempel");
            if (y!=0 && d>DateTime.DaysInMonth(y,m)) throw new FormatException("Ungültiges Datum");
            v.Timestamp=string.Format("{0:D4}-{1:D2}-{2:D2} {3:D2}:{4:D2}:{5:D2}",y,m,d,h,min,sec);
        }
        if ((flags & 4) != 0) v.Type=(byte)r.U8();
        r.End(); return v;
    }
    public static PlxReading Plx(byte[] bytes,PlxEncoding encoding) {
        var r=new Reader(bytes); int flags=r.U8();
        if ((flags & 0xE0) != 0) throw new FormatException("Unbekannte SpO2-Flags");
        Func<ushort,double> decode=encoding == PlxEncoding.LegacyIeeeHalf ? (Func<ushort,double>)HalfFloat : ShortFloat;
        var v=new PlxReading { Spo2=decode((ushort)r.U16()),Pulse=decode((ushort)r.U16()) };
        if ((flags & 1) != 0) { v.FastSpo2=decode((ushort)r.U16()); v.FastPulse=decode((ushort)r.U16()); }
        if ((flags & 2) != 0) { v.SlowSpo2=decode((ushort)r.U16()); v.SlowPulse=decode((ushort)r.U16()); }
        if ((flags & 4) != 0) v.MeasurementStatus=(ushort)r.U16();
        if ((flags & 8) != 0) v.DeviceStatus=r.U24();
        if ((flags & 16) != 0) v.Perfusion=decode((ushort)r.U16());
        r.End(); return v;
    }
    public static int Battery(byte[] bytes) {
        if (bytes==null || bytes.Length!=1 || bytes[0]>100) throw new FormatException("Ungültiger Akkuwert");
        return bytes[0];
    }
    public static SensorPacket Sensor(byte[] bytes) {
        if (bytes==null || bytes.Length<1) throw new FormatException("Sensorpaket fehlt");
        var v=new SensorPacket();
        if (bytes[0]==0x27 || bytes[0]==0x06) {
            if (bytes.Length<9) throw new FormatException("Signalqualität-Paket zu kurz");
            v.Quality=bytes[8]; v.IsMaximumQuality=bytes[0]==0x27;
        } else if (bytes[0]==0x07) {
            if (bytes.Length<2) throw new FormatException("Fehlercode-Paket zu kurz");
            v.ErrorCode=bytes[1];
        }
        return v;
    }
    public static string ErrorDescription(byte code) {
        switch(code) {
            case 0x0A:return "IR-Signalschwelle";
            case 0x0B:return "Rotlicht-Signalschwelle";
            case 0x0C:return "Beschleunigungssensor";
            case 0x0D:return "Unbekannte Akkukurve";
            case 0x0E:return "Grünlicht-Signalschwelle";
            case 0x11:case 0x3C:return "Temperatur-Sensorhinweis";
            case 0x3D:return "Unplausible Temperatur";
            default:return "Nicht dokumentierter Code";
        }
    }
}
