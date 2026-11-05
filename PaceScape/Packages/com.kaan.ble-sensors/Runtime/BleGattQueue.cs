using System;
using System.Collections.Generic;

// The queue can be tested without Unity, Android or a physical sensor.
public interface IBleGattBackend : IDisposable
{
    int Properties(string address, string service, string characteristic);
    bool Read(string address, string service, string characteristic);
    void Subscribe(string address, string service, string characteristic, int preferredMode);
}

public sealed class BleGattQueue : IDisposable
{
    public sealed class Request
    {
        public string Address, Service, Characteristic;
        public bool Subscribe;
        public int Mode;
    }
    readonly IBleGattBackend backend;
    readonly Queue<Request> pending = new Queue<Request>();
    Request active;
    float deadline;
    bool disposed;
    public event Action<Request, byte[], string> Completed;
    public event Action<string> Failed;
    public bool Idle => active == null && pending.Count == 0;
    public BleGattQueue(IBleGattBackend backend) { this.backend = backend; }
    public void Enqueue(Request request)
    {
        if (disposed) throw new ObjectDisposedException(nameof(BleGattQueue));
        request.Service = BleUuid.Normalize(request.Service);
        request.Characteristic = BleUuid.Normalize(request.Characteristic);
        pending.Enqueue(request);
    }
    public void Tick(float now)
    {
        if (disposed) return;
        if (active != null) { if (now > deadline) Fail("GATT-Zeitlimit: " + active.Characteristic); return; }
        // Start the next operation on the next frame, never reentrantly in a callback.
        while (pending.Count > 0)
        {
            var op = pending.Dequeue();
            try
            {
                int properties = backend.Properties(op.Address, op.Service, op.Characteristic);
                if ((properties & (op.Subscribe ? 0x30 : 2)) == 0)
                {
                    Completed?.Invoke(op, null, op.Subscribe ? "keine Updates unterstützt" : "READ nicht unterstützt");
                    continue;
                }
                active = op; deadline = now + 10;
                if (op.Subscribe) backend.Subscribe(op.Address, op.Service, op.Characteristic, op.Mode);
                else if (!backend.Read(op.Address, op.Service, op.Characteristic)) throw new InvalidOperationException("READ-Auftrag abgelehnt");
                return;
            }
            catch (Exception e) { Fail(e.Message); return; }
        }
    }
    public void Handle(string address,string service,string characteristic,string command,string descriptor,byte[] data,bool error,string errorText)
    {
        if (active == null || !string.Equals(address,active.Address,StringComparison.OrdinalIgnoreCase)
            || !string.Equals(service,active.Service,StringComparison.OrdinalIgnoreCase)
            || !string.Equals(characteristic,active.Characteristic,StringComparison.OrdinalIgnoreCase)) return;
        if (active.Subscribe ? command != "DescriptorWrite" || !string.Equals(descriptor,"00002902-0000-1000-8000-00805f9b34fb",StringComparison.OrdinalIgnoreCase)
                             : command != "ReadFromCharacteristic") return;
        if (error) { Fail(string.IsNullOrEmpty(errorText) ? "GATT-Fehler" : errorText); return; }
        var done = active; active = null;
        Completed?.Invoke(done, data, null);
    }
    void Fail(string error) { Clear(); Failed?.Invoke(error); }
    public void Clear() { pending.Clear(); active = null; }
    public void Dispose() { if (disposed) return; disposed=true; Clear(); backend.Dispose(); Completed=null; Failed=null; }
}
