// Checks project defaults and validation of dashboard server URLs.

using System;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    // Checks connection defaults and address validation without opening sockets or writing assets.
    public sealed class RemoteControlSettingsTests
    {
        private ScriptableObject _settings;

        [SetUp]
        public void SetUp()
        {
            Type type = Type.GetType("PaceScape.RemoteControl.RemoteControlSettings, Assembly-CSharp", true);
            _settings = ScriptableObject.CreateInstance(type);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void DefaultSettingsRetainTheUsbLoopbackConnection()
        {
            Assert.That(Property("ConnectionEnabled"), Is.True);
            Assert.That(Property("ServerUrl"), Is.EqualTo("ws://127.0.0.1:8787/ws"));
            Assert.That(Property("AccessToken"), Is.EqualTo(string.Empty));
        }

        [TestCase("ws://192.168.1.42:8787/ws")]
        [TestCase("wss://control.example/ws")]
        public void ValidConfigurationStoresAllValuesTogether(string address)
        {
            Assert.That(Configure(false, address, "local-test-token"), Is.True);
            Assert.That(Property("ConnectionEnabled"), Is.False);
            Assert.That(Property("ServerUrl"), Is.EqualTo(address));
            Assert.That(Property("AccessToken"), Is.EqualTo("local-test-token"));
        }

        [TestCase("")]
        [TestCase("http://localhost:8787/ws")]
        [TestCase("ws://localhost:8787")]
        [TestCase("ws://localhost:8787/other")]
        [TestCase("ws://user:password@localhost:8787/ws")]
        [TestCase("ws://localhost:8787/ws?token=secret")]
        [TestCase("ws://localhost:8787/ws#fragment")]
        public void InvalidConfigurationDoesNotOverwritePreviousValues(string address)
        {
            Assert.That(Configure(true, "ws://192.168.1.42:8787/ws", "existing-token"), Is.True);
            Assert.That(Configure(false, address, "replacement"), Is.False);
            Assert.That(Property("ConnectionEnabled"), Is.True);
            Assert.That(Property("ServerUrl"), Is.EqualTo("ws://192.168.1.42:8787/ws"));
            Assert.That(Property("AccessToken"), Is.EqualTo("existing-token"));
        }

        [Test]
        public void SurroundingWhitespaceIsRemovedAndNullTokenBecomesEmpty()
        {
            Assert.That(Configure(true, "  ws://127.0.0.1:9000/ws  ", null), Is.True);
            Assert.That(Property("ServerUrl"), Is.EqualTo("ws://127.0.0.1:9000/ws"));
            Assert.That(Property("AccessToken"), Is.EqualTo(string.Empty));
        }

        private bool Configure(bool connectionEnabled, string address, string token)
        {
            return (bool)_settings.GetType().GetMethod("TryConfigure").Invoke(_settings,
                new object[] { connectionEnabled, address, token });
        }

        private object Property(string name)
        {
            return _settings.GetType().GetProperty(name).GetValue(_settings);
        }
    }
}
