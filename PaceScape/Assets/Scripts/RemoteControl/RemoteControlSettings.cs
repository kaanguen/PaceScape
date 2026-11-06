using System;
using UnityEngine;

namespace PaceScape.RemoteControl
{
    /// <summary>Project defaults for the control connection, included in builds through Resources.</summary>
    public sealed class RemoteControlSettings : ScriptableObject
    {
        /// <summary>Gets the Resources lookup name used by the runtime client.</summary>
        public const string ResourceName = "PaceScapeConnection";

        /// <summary>Gets the loopback endpoint used with USB forwarding.</summary>
        public const string DefaultServerUrl = "ws://127.0.0.1:8787/ws";

        [SerializeField] private bool _connectionEnabled = true;
        [SerializeField] private string _serverUrl = DefaultServerUrl;
        [SerializeField] private string _accessToken = string.Empty;

        /// <summary>Gets whether the client should connect.</summary>
        public bool ConnectionEnabled => _connectionEnabled;

        /// <summary>Gets the complete WebSocket server endpoint.</summary>
        public string ServerUrl => _serverUrl;

        /// <summary>Gets the optional shared token for a trusted local network.</summary>
        public string AccessToken => _accessToken;

        /// <summary>Validates all connection values before updating the project defaults.</summary>
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

        /// <summary>Requires the relay's /ws endpoint without embedded credentials, query or fragment.</summary>
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
