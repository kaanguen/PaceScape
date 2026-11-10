using System;
using UnityEngine;

namespace PaceScape.Treadmill
{
    internal enum BeltOperation
    {
        Start,
        SetSpeed,
        Pause,
        Stop
    }

    /// <summary>Reads FTMS telemetry and handles explicitly authorized motor requests.</summary>
    [DisallowMultipleComponent]
    public sealed class TreadmillInput : MonoBehaviour
    {
        private const string FtmsControlPointUuid = "2AD9";
        private const string FtmsServiceUuid = "00001826-0000-1000-8000-00805f9b34fb";
        private const string FtmsControlPointFullUuid = "00002ad9-0000-1000-8000-00805f9b34fb";
        private const string FtmsSpeedRangeUuid = "00002ad4-0000-1000-8000-00805f9b34fb";
        private const byte SetTargetSpeed = 0x02;
        private const byte RequestControl = 0x00;
        private const byte StartOrResume = 0x07;
        private const byte StopOrPause = 0x08;
        private const byte StopParameter = 0x01;
        private const byte PauseParameter = 0x02;
        private const byte ResponseCode = 0x80;
        private const byte Success = 0x01;

        private BleConnection _connection;
        private SensorMeasurements _measurements;
        private BleDeviceProfile _profile;
        private Action<string> _controlCompleted;
        private Func<bool> _canContinueControl;
        private float _controlDeadline;
        private byte _expectedResponse;
        private bool _waitingForSubscription;
        private bool _operationSent;
        private BeltOperation _operation;
        private int _targetSpeedUnits;
        private int _minSpeedUnits;
        private int _maxSpeedUnits;
        private int _speedStepUnits;
        private bool _speedRangeReadPending;

        /// <summary>Raised when a disconnect or invalid notification breaks measurement continuity.</summary>
        public event Action MeasurementInterrupted;

        private void Awake()
        {
            _profile = TreadmillProfileDefaults.Create();
            _connection = gameObject.AddComponent<BleConnection>();
            _connection.profile = _profile;
            _connection.GattReady += OnGattReady;
            _connection.OperationCompleted += OnGattOperationCompleted;
            _connection.DataReceived += OnGattDataReceived;
            _measurements = gameObject.AddComponent<SensorMeasurements>();
            _measurements.bluetooth = _connection;
            _measurements.TreadmillSignalLost += OnMeasurementInterrupted;
        }

        internal bool TryControlBelt(string deviceId, BeltOperation operation, int targetSpeedUnits, Func<bool> canContinue,
            Action<string> completed, out string error)
        {
            error = null;
            if (_controlCompleted != null || _speedRangeReadPending || !IsBeltReady(operation, targetSpeedUnits) ||
                !string.Equals(deviceId, _connection.ConnectedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                error = "Das ausgewählte Laufband ist für diesen FTMS-Befehl nicht bereit.";
                return false;
            }

            _connection.Subscribe(FtmsServiceUuid, FtmsControlPointFullUuid, BleUpdateMode.IndicatePreferred);
            _canContinueControl = canContinue;
            _controlCompleted = completed;
            _operation = operation;
            _targetSpeedUnits = targetSpeedUnits;
            _waitingForSubscription = true;
            _operationSent = false;
            _controlDeadline = Time.realtimeSinceStartup + 5f;
            return true;
        }

        private void OnGattReady(string deviceId)
        {
            _speedStepUnits = 0;
            _speedRangeReadPending = false;
        }

        private bool IsBeltReady(BeltOperation operation, int targetSpeedUnits)
        {
            if (!isActiveAndEnabled || !_connection.Connected ||
                !_connection.HasCharacteristic(FtmsServiceUuid, FtmsControlPointFullUuid))
            {
                return false;
            }

            // Stopping must remain available even if speed telemetry has gone stale.
            if (operation == BeltOperation.Pause || operation == BeltOperation.Stop)
            {
                return true;
            }

            if (!TryGetSpeedKmh(out double speed))
            {
                return false;
            }

            return operation == BeltOperation.Start ? speed == 0 : speed > 0 && targetSpeedUnits > 0 &&
                _speedStepUnits > 0 && targetSpeedUnits >= _minSpeedUnits && targetSpeedUnits <= _maxSpeedUnits &&
                (targetSpeedUnits - _minSpeedUnits) % _speedStepUnits == 0;
        }

        private void Update()
        {
            if (_controlCompleted != null && (!_connection.Connected || !_canContinueControl() ||
                Time.realtimeSinceStartup >= _controlDeadline))
            {
                AbortControl();
            }
        }

        private void OnGattOperationCompleted(string service, string characteristic, bool subscribed, string error)
        {
            if (!subscribed && service == FtmsServiceUuid && characteristic == FtmsSpeedRangeUuid)
            {
                _speedRangeReadPending = false;
                _connection.ShowStatus(error != null || _speedStepUnits == 0
                    ? "FTMS-Tempobereich konnte nicht gelesen werden."
                    : "FTMS-Tempobereich geladen.");
                return;
            }

            if (_controlCompleted == null || !_waitingForSubscription || !subscribed ||
                service != FtmsServiceUuid || characteristic != FtmsControlPointFullUuid)
            {
                return;
            }

            _waitingForSubscription = false;
            if (error != null)
            {
                CompleteControl(error);
                return;
            }

            SendControlStep(RequestControl);
        }

        private void OnGattDataReceived(string service, string characteristic, byte[] data)
        {
            if (service == FtmsServiceUuid && characteristic == FtmsSpeedRangeUuid && data.Length == 6)
            {
                _minSpeedUnits = data[0] | (data[1] << 8);
                _maxSpeedUnits = data[2] | (data[3] << 8);
                _speedStepUnits = _maxSpeedUnits >= _minSpeedUnits ? data[4] | (data[5] << 8) : 0;
                return;
            }

            if (_controlCompleted == null || _waitingForSubscription ||
                !string.Equals(service, FtmsServiceUuid, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(characteristic, FtmsControlPointFullUuid, StringComparison.OrdinalIgnoreCase) ||
                data.Length < 3 || data[0] != ResponseCode || data[1] != _expectedResponse)
            {
                return;
            }

            if (data[2] != Success)
            {
                CompleteControl($"Treadmill rejected FTMS operation 0x{data[1]:X2} (result 0x{data[2]:X2}).");
            }
            else if (_expectedResponse == RequestControl)
            {
                SendControlStep(_operation == BeltOperation.Start ? StartOrResume :
                    _operation == BeltOperation.SetSpeed ? SetTargetSpeed : StopOrPause);
            }
            else
            {
                CompleteControl(null);
            }
        }

        private void SendControlStep(byte opcode)
        {
            if (Time.realtimeSinceStartup >= _controlDeadline || !_canContinueControl() || !IsBeltReady(_operation, _targetSpeedUnits))
            {
                AbortControl();
                return;
            }

            _expectedResponse = opcode;
            try
            {
                byte[] command = opcode switch
                {
                    SetTargetSpeed => new[] { opcode, (byte)(_targetSpeedUnits & 0xff), (byte)(_targetSpeedUnits >> 8) },
                    StopOrPause => new[] { opcode, _operation == BeltOperation.Pause ? PauseParameter : StopParameter },
                    _ => new[] { opcode }
                };
                if (!_connection.TryWriteIdle(FtmsServiceUuid, FtmsControlPointFullUuid, command))
                {
                    CompleteControl("Bluetooth is busy. The treadmill command was not sent.");
                    return;
                }

                _operationSent = opcode != RequestControl;
            }
            catch (Exception)
            {
                _operationSent = opcode != RequestControl;
                AbortControl();
            }
        }

        private void AbortControl()
        {
            if (_controlCompleted == null)
            {
                return;
            }

            CompleteControl(_operationSent
                ? "No FTMS confirmation. Check the treadmill console before trying again."
                : "FTMS operation aborted before sending the motor command.");
            _connection.Disconnect();
        }

        private void CompleteControl(string error)
        {
            Action<string> completed = _controlCompleted;
            _controlCompleted = null;
            _canContinueControl = null;
            completed(error);
        }

        /// <summary>Reports a fresh speed without treating stopped belts as missing data.</summary>
        public bool TryGetSpeedKmh(out double speedKmh)
        {
            speedKmh = 0;
            return isActiveAndEnabled && _measurements != null &&
                _measurements.TryGetTreadmillSpeedKmh(out speedKmh) &&
                !double.IsNaN(speedKmh) && !double.IsInfinity(speedKmh) && speedKmh >= 0;
        }

        /// <summary>Applies operator-selected BLE actions. Connection success only means the request was accepted.</summary>
        public bool TryApply(string command, string deviceId, out string error)
        {
            error = null;
            if (!isActiveAndEnabled || _connection == null)
            {
                error = "Treadmill input is unavailable.";
                return false;
            }

            switch (command)
            {
                case "treadmill.scan":
                    return _connection.TryStartScan(out error);
                case "treadmill.scan.stop":
                    _connection.StopScan();
                    return true;
                case "treadmill.connect":
                    if (!_connection.TryConnectDevice(deviceId, out error))
                    {
                        return false;
                    }
                    return true;
                case "treadmill.disconnect":
                    _connection.Disconnect();
                    return true;
                case "treadmill.readSpeedRange":
                    if (_speedRangeReadPending || _controlCompleted != null ||
                        !TryGetSpeedKmh(out double speed) || speed != 0 ||
                        !string.Equals(deviceId, _connection.ConnectedDeviceId, StringComparison.OrdinalIgnoreCase) ||
                        !_connection.HasCharacteristic(FtmsServiceUuid, FtmsSpeedRangeUuid))
                    {
                        error = "Tempobereich nur bei verbundenem, stehendem Laufband abfragen.";
                        return false;
                    }

                    _connection.Read(FtmsServiceUuid, FtmsSpeedRangeUuid);
                    _speedRangeReadPending = true;
                    _connection.ShowStatus("FTMS-Tempobereich wird geladen …");
                    return true;
                default:
                    error = "Unsupported treadmill command.";
                    return false;
            }
        }

        internal TreadmillState CaptureState()
        {
            var state = new TreadmillState
            {
                supported = Application.platform == RuntimePlatform.Android && !Application.isEditor,
                motorStartVersion = 4,
                status = _connection != null ? _connection.Status : "Not initialized."
            };
            if (_connection == null)
            {
                return state;
            }

            state.scanning = _connection.Scanning || _connection.Initializing;
            state.busy = _connection.Busy;
            state.connected = _connection.Connected;
            state.deviceId = _connection.ConnectedDeviceId;
            state.deviceName = _connection.ConnectedDeviceName;
            state.hasTreadmillData = _measurements.HasTreadmillData;
            state.hasFtmsControlPoint = state.connected &&
                _connection.HasCharacteristic(FtmsTreadmillData.ServiceUuid, FtmsControlPointUuid);
            state.canReadSpeedRange = state.connected && !_speedRangeReadPending &&
                _connection.HasCharacteristic(FtmsServiceUuid, FtmsSpeedRangeUuid);
            if (state.connected && _speedStepUnits > 0)
            {
                state.minSpeedUnits = _minSpeedUnits;
                state.maxSpeedUnits = _maxSpeedUnits;
                state.speedStepUnits = _speedStepUnits;
            }
            state.hasSpeed = TryGetSpeedKmh(out double speed);
            state.speedKmh = state.hasSpeed ? (float)speed : 0f;
            state.hasDistance = _measurements.TryGetTreadmillTotalDistanceMetres(out uint distance);
            state.totalDistanceMetres = state.hasDistance ? distance : 0;
            state.devices = new TreadmillDevice[_connection.DeviceIds.Count];
            for (int i = 0; i < state.devices.Length; i++)
            {
                state.devices[i] = new TreadmillDevice
                {
                    id = _connection.DeviceIds[i],
                    name = _connection.DeviceNames[i]
                };
            }
            return state;
        }

        private void OnDisable()
        {
            AbortControl();
            _connection?.Disconnect();
        }

        private void OnMeasurementInterrupted()
        {
            MeasurementInterrupted?.Invoke();
        }

        private void OnDestroy()
        {
            if (_measurements != null)
            {
                _measurements.TreadmillSignalLost -= OnMeasurementInterrupted;
            }
            if (_profile != null)
            {
                Destroy(_profile);
            }
        }
    }

    // Wire fields match the existing camelCase dashboard contract.
    [Serializable]
    internal sealed class TreadmillState
    {
        public bool supported;
        public string status;
        public bool scanning;
        public bool busy;
        public bool connected;
        public bool hasTreadmillData;
        public bool hasFtmsControlPoint;
        public bool canReadSpeedRange;
        public int motorStartVersion;
        public int minSpeedUnits;
        public int maxSpeedUnits;
        public int speedStepUnits;
        public string deviceId;
        public string deviceName;
        public bool hasSpeed;
        public float speedKmh;
        public bool hasDistance;
        public uint totalDistanceMetres;
        public TreadmillDevice[] devices = Array.Empty<TreadmillDevice>();
    }

    [Serializable]
    internal sealed class TreadmillDevice
    {
        public string id;
        public string name;
    }
}
