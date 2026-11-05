using System;
using UnityEngine;

namespace PaceScape.Treadmill
{
    /// <summary>Reads FTMS through the user's BLE package; never sends motor control commands.</summary>
    [DisallowMultipleComponent]
    public sealed class TreadmillInput : MonoBehaviour
    {
        private BleConnection _connection;
        private SensorMeasurements _measurements;
        private BleDeviceProfile _profile;

        /// <summary>Gets whether runner speed is owned by the treadmill, including while its signal is missing.</summary>
        public bool FollowTreadmill { get; private set; }

        private void Awake()
        {
            _profile = TreadmillProfileDefaults.Create();
            _connection = gameObject.AddComponent<BleConnection>();
            _connection.profile = _profile;
            _measurements = gameObject.AddComponent<SensorMeasurements>();
            _measurements.bluetooth = _connection;
        }

        /// <summary>Reports a fresh speed without treating stopped belts as missing data.</summary>
        public bool TryGetSpeedKmh(out double speedKmh)
        {
            speedKmh = 0;
            return isActiveAndEnabled && _measurements != null &&
                _measurements.TryGetTreadmillSpeedKmh(out speedKmh) &&
                !double.IsNaN(speedKmh) && !double.IsInfinity(speedKmh) && speedKmh >= 0;
        }

        /// <summary>Gets the effective pace; zero denotes a stationary belt or unavailable signal.</summary>
        public float GetRunnerPace(float manualPace)
        {
            if (!FollowTreadmill)
            {
                return manualPace;
            }

            return TryGetSpeedKmh(out double speed) && speed > 0 ? (float)(60.0 / speed) : 0f;
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
                    FollowTreadmill = true;
                    return true;
                case "treadmill.disconnect":
                    _connection.Disconnect();
                    return true;
                case "treadmill.manual":
                    // Disconnect first: switching sources requires a deliberate new run/resume.
                    _connection.Disconnect();
                    FollowTreadmill = false;
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
                followTreadmill = FollowTreadmill,
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
            _connection?.Disconnect();
        }

        private void OnDestroy()
        {
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
        public bool followTreadmill;
        public string status;
        public bool scanning;
        public bool busy;
        public bool connected;
        public bool hasTreadmillData;
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
