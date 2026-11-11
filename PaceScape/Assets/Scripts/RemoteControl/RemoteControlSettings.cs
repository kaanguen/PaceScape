// Stores and validates the project's default dashboard connection settings.

using System;
using UnityEngine;

namespace PaceScape.RemoteControl
{
    // Project defaults for the control connection, included in builds through Resources.
    public sealed class RemoteControlSettings : ScriptableObject
    {
        // Gets the Resources lookup name used by the runtime client.
        public const string ResourceName = "PaceScapeConnection";

        // Gets the loopback endpoint used with USB forwarding.
        public const string DefaultServerUrl = "ws://127.0.0.1:8787/ws";

        [SerializeField] private bool _connectionEnabled = true;
        [SerializeField] private string _serverUrl = DefaultServerUrl;
        [SerializeField] private string _accessToken = string.Empty;
        [SerializeField] private bool _allowLanMotorControl;

        // Gets whether the client should connect.
        public bool ConnectionEnabled => _connectionEnabled;

        // Gets the complete WebSocket server endpoint.
        public string ServerUrl => _serverUrl;

        // Gets the optional shared token for a trusted local network.
        public string AccessToken => _accessToken;

        // Gets or sets whether the configured IP endpoint may issue motor commands over LAN.
        public bool AllowLanMotorControl
        {
            get => _allowLanMotorControl;
            set => _allowLanMotorControl = value;
        }

        // Validates all connection values before updating the project defaults.
        public bool TryConfigure(bool connectionEnabled, string serverUrl, string accessToken)
        {
            if (!TryParseServerUrl(serverUrl, out Uri uri))
            {
                return false;
            }

            _connectionEnabled = connectionEnabled;
            _serverUrl = uri.AbsoluteUri;
            _accessToken = accessToken ?? string.Empty;
            return true;
        }

        // Requires the relay's /ws endpoint without embedded credentials, query or fragment.
        public static bool TryParseServerUrl(string serverUrl, out Uri uri)
        {
            if (!Uri.TryCreate(serverUrl?.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }

            return (uri.Scheme == "ws" || uri.Scheme == "wss") && !string.IsNullOrEmpty(uri.Host) &&
                uri.AbsolutePath == "/ws" && string.IsNullOrEmpty(uri.UserInfo) &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }
    }
}
