using System;
using UnityEngine;

namespace MondmadeVJ0
{
    /// <summary>Applies the documented four-float OSC transport packet to an external-clock transport.</summary>
    [DisallowMultipleComponent]
    public sealed class OscTransportSyncAdapter : MonoBehaviour, IRuntimeModule
    {
        public const string DefaultAddress = "/mondmade/transport";
        public const int MaximumExactWholeBeat = 16777215;

        [SerializeField] private string address = DefaultAddress;
        private RuntimeServices services;
        private OscHub hub;
        private IExternalTransportClock transport;

        public string ModuleName => nameof(OscTransportSyncAdapter);
        public long AcceptedPackets { get; private set; }
        public long RejectedPackets { get; private set; }

        public void Register(RuntimeServices runtimeServices) => services = runtimeServices;

        public void Initialize(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            if (!OscAddress.IsValid(address)) throw new InvalidOperationException($"OSC transport address '{address}' is invalid.");
            hub = services.Get<OscHub>();
            transport = services.Get<IExternalTransportClock>();
            hub.MessageReceived += OnMessage;
        }

        public void DrainInputQueue(RuntimeServices runtimeServices, int maxEvents) { }
        public void Tick(RuntimeServices runtimeServices, double deltaTime) { }
        public void FlushFrame(RuntimeServices runtimeServices) { }

        private void OnMessage(OscPacket packet)
        {
            if (!string.Equals(packet.Address, address, StringComparison.Ordinal)) return;
            if (!packet.HasNumericShape("ffff"))
            {
                Reject("OSC transport packet must contain exactly four float arguments (ffff).");
                return;
            }

            var bpm = packet.Float0;
            var wholeBeat = packet.Float1;
            var fractionalBeat = packet.Float2;
            var runningValue = packet.Float3;
            if (!Finite(bpm) || bpm < 30f || bpm > 300f ||
                !Finite(wholeBeat) || wholeBeat < 0f || wholeBeat > MaximumExactWholeBeat || wholeBeat != Mathf.Floor(wholeBeat) ||
                !Finite(fractionalBeat) || fractionalBeat < 0f || fractionalBeat >= 1f ||
                (runningValue != 0f && runningValue != 1f))
            {
                Reject("OSC transport packet values are outside the documented range.");
                return;
            }

            transport.SetExternalClock(bpm, (double)wholeBeat + fractionalBeat, runningValue == 1f, Time.realtimeSinceStartupAsDouble);
            AcceptedPackets++;
        }

        private void Reject(string message)
        {
            RejectedPackets++;
            services?.Report("OSC_TRANSPORT_PACKET", message, nodeId: address);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Shutdown(RuntimeServices runtimeServices)
        {
            if (hub != null) hub.MessageReceived -= OnMessage;
            hub = null;
            transport = null;
            services = null;
        }
    }
}
