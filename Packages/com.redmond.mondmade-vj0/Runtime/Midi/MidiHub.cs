using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Minis;
using RtMidi;
using UnityEngine;
using UnityEngine.InputSystem;
using R3;

namespace MondmadeVJ0
{
    public enum MidiAddressType { Note, ControlChange, PolyAftertouch, ChannelPressure, PitchBend }
    public enum MidiControlType { Button, VelocityButton, AbsoluteFader, AbsoluteKnob, RelativeEncoder, XY, PitchBend, Aftertouch }
    public enum MidiRelativeMode { TwosComplement, BinaryOffset, SignMagnitude }
    public enum MidiInputBackend { Minis, RtMidiRaw }
    public enum MidiLedLayer { Base = 0, State = 100, Beat = 200, Interaction = 300, Alert = 400, Custom = 500 }
    public enum MidiLedEffect { Solid, Blink, Pulse }

    [Serializable]
    public sealed class MidiSurfaceInstance
    {
        [SerializeField] private string instanceId = Guid.NewGuid().ToString("N");
        [SerializeField] private MidiSurfaceProfile profile;
        [SerializeField] private string productName;
        [SerializeField] private int deviceOrdinal;
        [SerializeField] private MidiInputBackend inputBackend;
        [SerializeField] private int rawInputPortOrdinal = -1;
        [SerializeField] private int outputPortOrdinal = -1;
        public string InstanceId => instanceId;
        public MidiSurfaceProfile Profile => profile;
        public string ProductName => !string.IsNullOrEmpty(productName) ? productName : profile == null ? string.Empty : profile.ProductName;
        public int DeviceOrdinal => Math.Max(0, deviceOrdinal);
        public MidiInputBackend InputBackend => inputBackend;
        public int RawInputPortOrdinal => rawInputPortOrdinal;
        public int OutputPortOrdinal => outputPortOrdinal;
        public void Configure(MidiSurfaceProfile surfaceProfile, string product, int ordinal = 0, MidiInputBackend backend = MidiInputBackend.Minis, int rawInputOrdinal = -1, int outputOrdinal = -1)
        { profile = surfaceProfile; productName = product; deviceOrdinal = ordinal; inputBackend = backend; rawInputPortOrdinal = rawInputOrdinal; outputPortOrdinal = outputOrdinal; if (string.IsNullOrEmpty(instanceId)) instanceId = Guid.NewGuid().ToString("N"); }
    }

    [Serializable]
    public struct MidiControlReference
    {
        [SerializeField] private string surfaceInstanceId;
        [SerializeField] private string controlId;
        public string SurfaceInstanceId => surfaceInstanceId;
        public string ControlId => controlId;
        public MidiControlReference(string surface, string control) { surfaceInstanceId = surface; controlId = control; }
        public override string ToString() => (surfaceInstanceId ?? "") + "/" + (controlId ?? "");
    }

    [Serializable]
    public struct MidiLedStyle
    {
        public Color color;
        [Range(0f, 1f)] public float brightness;
        public MidiLedEffect effect;
        public float durationSeconds;
        public static MidiLedStyle Off => new MidiLedStyle { color = Color.black, brightness = 0f, effect = MidiLedEffect.Solid };
    }

    public readonly struct MidiControlState
    {
        public readonly float Value;
        public readonly float Velocity;
        public readonly bool Pressed;
        public readonly float Pressure;
        public readonly int GroupIndex;
        public MidiControlState(float value, float velocity, bool pressed, float pressure, int groupIndex)
        { Value = value; Velocity = velocity; Pressed = pressed; Pressure = pressure; GroupIndex = groupIndex; }
    }

    public readonly struct MidiControlChange
    {
        public readonly MidiControlReference Reference;
        public readonly MidiControlState State;
        public readonly bool PressedDown;
        public readonly bool Released;
        public MidiControlChange(MidiControlReference reference, MidiControlState state, bool pressedDown, bool released)
        { Reference = reference; State = state; PressedDown = pressedDown; Released = released; }
    }

    public readonly struct MidiRawMessage
    {
        public readonly string SurfaceInstanceId;
        public readonly MidiAddressType AddressType;
        public readonly int Channel;
        public readonly int Number;
        public readonly float Value;
        public MidiRawMessage(string surface, MidiAddressType type, int channel, int number, float value)
        { SurfaceInstanceId = surface; AddressType = type; Channel = channel; Number = number; Value = value; }
    }

    [DisallowMultipleComponent]
    public sealed class MidiHub : MonoBehaviour, IRuntimeModule
    {
        private const int NormalCapacity = 8192;
        private const int ClockCapacity = 8192;
        private const int SysExMaxLength = 64 * 1024;
        private const int RawQueueByteCapacity = 8 * 1024 * 1024;
        [SerializeField] private MidiSurfaceInstance[] surfaces = Array.Empty<MidiSurfaceInstance>();
        private readonly object queueLock = new object();
        private readonly Queue<InputPacket> normalQueue = new Queue<InputPacket>();
        private readonly Queue<InputPacket> clockQueue = new Queue<InputPacket>();
        private readonly Dictionary<string, ControlRuntime> controls = new Dictionary<string, ControlRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, MidiSurfaceRuntime> surfaceRuntime = new Dictionary<string, MidiSurfaceRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, LayerValue>> layers = new Dictionary<string, Dictionary<string, LayerValue>>(StringComparer.Ordinal);
        private readonly Dictionary<string, MidiLedPayload> sentPayloads = new Dictionary<string, MidiLedPayload>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> outputQueueKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object callbackLock = new object();
        private int queuedRawBytes;
        private Action<MidiControlChange>[] controlChangedCallbacks = Array.Empty<Action<MidiControlChange>>();
        private Action<MidiRawMessage>[] rawInputCallbacks = Array.Empty<Action<MidiRawMessage>>();
        private long droppedNormal, droppedClock, sequence;
        private bool initialized, panicLatched;
        private double nextScanTime;
        private RuntimeServices services;
        private string outputQueueOwner;

        public string ModuleName => nameof(MidiHub);
        public long DroppedNormalMessages => Interlocked.Read(ref droppedNormal);
        public long DroppedClockMessages => Interlocked.Read(ref droppedClock);
        public long DroppedOutputMessages
        {
            get
            {
                var total = 0L;
                var ports = new HashSet<int>();
                foreach (var runtime in surfaceRuntime.Values)
                    if (runtime.Output != null && ports.Add(runtime.Output.PortOrdinal)) total += runtime.Output.DroppedMessages;
                return total;
            }
        }
        public bool PanicLatched => panicLatched;
        public IReadOnlyList<MidiSurfaceInstance> Surfaces => surfaces;
        public event Action<MidiControlChange> ControlChanged
        {
            add { lock (callbackLock) controlChangedCallbacks = AddCallback(controlChangedCallbacks, value); }
            remove { lock (callbackLock) controlChangedCallbacks = RemoveCallback(controlChangedCallbacks, value); }
        }
        public event Action<MidiRawMessage> RawInputObserved
        {
            add { lock (callbackLock) rawInputCallbacks = AddCallback(rawInputCallbacks, value); }
            remove { lock (callbackLock) rawInputCallbacks = RemoveCallback(rawInputCallbacks, value); }
        }

        public string GetSurfaceStatus(string instanceId) => surfaceRuntime.TryGetValue(instanceId ?? string.Empty, out var runtime) ? runtime.Status : "Not initialized";

        public bool TryGetControlDefinition(MidiControlReference reference, out MidiSurfaceControl definition)
        {
            if (controls.TryGetValue(Key(reference.SurfaceInstanceId, reference.ControlId), out var control))
            { definition = control.Definition; return true; }
            definition = null;
            return false;
        }

        public bool TrySetLearnedAddress(string surfaceInstanceId, string controlId, MidiAddressType addressType, int channel, int number)
        {
            if (!TryGetControlDefinition(new MidiControlReference(surfaceInstanceId, controlId), out var definition)) return false;
            definition.inputAddressType = addressType;
            definition.channel = Mathf.Clamp(channel, 0, 15);
            definition.inputNumber = Mathf.Clamp(number, 0, 127);
            if (definition.hasLed)
            {
                definition.outputAddressType = addressType == MidiAddressType.ControlChange ? MidiAddressType.ControlChange : MidiAddressType.Note;
                definition.outputNumber = definition.inputNumber;
                definition.outputChannel = definition.channel;
            }
            return true;
        }

        public void Register(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            runtimeServices.Register(this);
        }

        public void Initialize(RuntimeServices runtimeServices)
        {
            services = runtimeServices;
            initialized = true;
            outputQueueOwner = Guid.NewGuid().ToString("N");
            controls.Clear();
            surfaceRuntime.Clear();
            layers.Clear();
            sentPayloads.Clear();
            outputQueueKeys.Clear();
            if (surfaces != null)
                foreach (var instance in surfaces)
                {
                    if (instance?.Profile == null) { services.Report("MIDI_PROFILE", "A MIDI surface has no profile."); continue; }
                    if (surfaceRuntime.ContainsKey(instance.InstanceId)) { services.Report("MIDI_SURFACE_ID", "Surface instance IDs must be unique."); continue; }
                    var runtime = new MidiSurfaceRuntime(instance);
                    surfaceRuntime.Add(instance.InstanceId, runtime);
                    foreach (var definition in instance.Profile.Controls)
                    {
                        if (definition == null || string.IsNullOrWhiteSpace(definition.id)) continue;
                        var key = Key(instance.InstanceId, definition.id);
                        if (controls.ContainsKey(key)) { services.Report("MIDI_CONTROL_ID", $"Duplicate logical control ID '{definition.id}'."); continue; }
                        var controlRuntime = new ControlRuntime(definition, instance.InstanceId, key);
                        controls.Add(key, controlRuntime);
                        runtime.ControlsById.Add(definition.id, controlRuntime);
                    }
                    if (instance.OutputPortOrdinal >= 0) TryOpenOutput(runtime);
                    if (instance.InputBackend == MidiInputBackend.RtMidiRaw) TryOpenRawInput(runtime);
                }
            InputSystem.onDeviceChange += OnDeviceChange;
            ScanDevices();
        }

        public void DrainInputQueue(RuntimeServices runtimeServices, int maxEvents)
        {
            maxEvents = Math.Max(0, maxEvents);
            int clockRemaining;
            int normalRemaining;
            lock (queueLock)
            {
                clockRemaining = Math.Min(clockQueue.Count, maxEvents);
                normalRemaining = Math.Min(normalQueue.Count, maxEvents - clockRemaining);
            }
            var processed = 0;
            var targetCount = clockRemaining + normalRemaining;
            while (processed < targetCount)
            {
                InputPacket packet;
                lock (queueLock)
                {
                    if (clockRemaining > 0 && clockQueue.Count > 0) { packet = clockQueue.Dequeue(); clockRemaining--; }
                    else if (normalRemaining > 0 && normalQueue.Count > 0) { packet = normalQueue.Dequeue(); normalRemaining--; }
                    else break;
                }
                try
                {
                    if (packet.Raw != null) ProcessRaw(packet);
                    else ProcessControl(packet);
                }
                finally
                {
                    if (packet.Raw != null)
                    {
                        lock (queueLock)
                        {
                            var rawBufferBytes = packet.Raw.Length;
                            ArrayPool<byte>.Shared.Return(packet.Raw);
                            queuedRawBytes -= rawBufferBytes;
                        }
                    }
                }
                processed++;
            }
        }

        public void Tick(RuntimeServices runtimeServices, double deltaTime)
        {
            if (!initialized) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (now < nextScanTime) return;
            nextScanTime = now + 1d;
            ScanDevices();
        }

        public void FlushFrame(RuntimeServices runtimeServices)
        {
            foreach (var pair in surfaceRuntime)
            {
                var surfaceId = pair.Key;
                var runtime = pair.Value;
                var profile = runtime.Instance.Profile;
                if (profile == null || runtime.Output == null) continue;
                var connectionGeneration = runtime.Output.ConnectionGeneration;
                if (connectionGeneration != runtime.OutputGeneration)
                {
                    runtime.OutputGeneration = connectionGeneration;
                    runtime.ForceFullResync = true;
                    RemoveSentPayloads(runtime);
                }
                if (!runtime.Output.IsOk) runtime.ForceFullResync = true;
                var queuedAll = true;
                runtime.PendingApcPadUpdates.Clear();
                runtime.PendingApcPadStates.Clear();
                foreach (var control in profile.Controls)
                {
                    if (control == null || !control.hasLed) continue;
                    if (!runtime.ControlsById.TryGetValue(control.id, out var controlRuntime)) continue;
                    var key = controlRuntime.Key;
                    var style = panicLatched ? MidiLedStyle.Off : ResolveLayer(key);
                    var payload = MidiLedEncoder.Encode(profile.DriverId, control, style);
                    if (!runtime.ForceFullResync && sentPayloads.TryGetValue(key, out var last) && last.Equals(payload)) continue;
                    try
                    {
                        var queueKey = GetOutputQueueKey(key);
                        if (IsApcRgbPad(profile, control) && style.effect == MidiLedEffect.Solid)
                        {
                            runtime.PendingApcPadUpdates.Add(EncodeApcPadLed(queueKey, payload));
                            runtime.PendingApcPadStates.Add(new PendingApcPadState(key, control, style, payload));
                            continue;
                        }

                        var accepted = runtime.Output.QueueLed(queueKey, payload.Status, payload.Number, payload.Value);
                        if (!accepted)
                        {
                            queuedAll = false;
                            services?.Report("MIDI_OUTPUT_QUEUE", $"LED output queue rejected {surfaceId}/{control.id}.", nodeId: control.id);
                        }
                        else
                        {
                            sentPayloads[key] = payload;
                            services?.RecordMidiLedQueued(surfaceId, runtime.Instance.ProductName, control.id, control.outputChannel, payload.Number,
                                payload.Value, Mathf.RoundToInt(Mathf.Clamp01(style.color.r) * 255f),
                                Mathf.RoundToInt(Mathf.Clamp01(style.color.g) * 255f),
                                Mathf.RoundToInt(Mathf.Clamp01(style.color.b) * 255f), style.brightness, (int)style.effect);
                        }
                    }
                    catch (Exception exception)
                    {
                        services?.Report("MIDI_OUTPUT", $"LED send failed for {surfaceId}/{control.id}.", nodeId: control.id, exception: exception);
                        runtime.ForceFullResync = true;
                        break;
                    }
                }
                if (runtime.PendingApcPadUpdates.Count > 0)
                {
                    try
                    {
                        if (!runtime.Output.QueueApcPadLeds(runtime.PendingApcPadUpdates))
                        {
                            queuedAll = false;
                            services?.Report("MIDI_OUTPUT_QUEUE", $"LED output queue rejected an APC pad batch for {surfaceId}.");
                        }
                        else
                        {
                            foreach (var pending in runtime.PendingApcPadStates)
                            {
                                sentPayloads[pending.ControlKey] = pending.Payload;
                                services?.RecordMidiLedQueued(surfaceId, runtime.Instance.ProductName, pending.Control.id,
                                    pending.Control.outputChannel, pending.Payload.Number, pending.Payload.Value,
                                    Mathf.RoundToInt(Mathf.Clamp01(pending.Style.color.r) * 255f),
                                    Mathf.RoundToInt(Mathf.Clamp01(pending.Style.color.g) * 255f),
                                    Mathf.RoundToInt(Mathf.Clamp01(pending.Style.color.b) * 255f),
                                    pending.Style.brightness, (int)pending.Style.effect);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        services?.Report("MIDI_OUTPUT", $"LED batch send failed for {surfaceId}.", exception: exception);
                        queuedAll = false;
                        runtime.ForceFullResync = true;
                    }
                }
                if (runtime.Output.IsOk && queuedAll) runtime.ForceFullResync = false;
            }
        }

        public void Shutdown(RuntimeServices runtimeServices)
        {
            initialized = false;
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var runtime in surfaceRuntime.Values) ReleaseSurface(runtime, sendAllOff: true);
            surfaceRuntime.Clear();
            controls.Clear();
            layers.Clear();
            sentPayloads.Clear();
            outputQueueKeys.Clear();
            outputQueueOwner = null;
            lock (callbackLock)
            {
                controlChangedCallbacks = Array.Empty<Action<MidiControlChange>>();
                rawInputCallbacks = Array.Empty<Action<MidiRawMessage>>();
            }
            lock (queueLock)
            {
                while (normalQueue.Count > 0) ReturnQueuedRaw(normalQueue.Dequeue());
                while (clockQueue.Count > 0) ReturnQueuedRaw(clockQueue.Dequeue());
            }
        }

        public MidiControlState GetState(MidiControlReference reference)
        {
            return controls.TryGetValue(Key(reference.SurfaceInstanceId, reference.ControlId), out var control)
                ? control.State : default;
        }

#if UNITY_EDITOR
        internal bool QueueEditorInput(MidiControlReference reference, float normalizedValue)
        {
            if (!initialized || !controls.TryGetValue(Key(reference.SurfaceInstanceId, reference.ControlId), out var control)) return false;
            var definition = control.Definition;
            var value = definition.inputAddressType == MidiAddressType.PitchBend
                ? Mathf.Clamp(normalizedValue, -1f, 1f)
                : Mathf.Clamp01(normalizedValue);
            if (definition.inputAddressType == MidiAddressType.PitchBend)
            {
                var rawPitch = Mathf.Clamp(Mathf.RoundToInt(value * 8192f + 8192f), 0, 16383);
                value = (rawPitch - 8192) / 8192f;
            }
            else value = Mathf.Round(value * 127f) / 127f;
            var number = definition.inputAddressType is MidiAddressType.PitchBend or MidiAddressType.ChannelPressure
                ? 0
                : definition.inputNumber;
            var channel = definition.channel >= 0 ? definition.channel : 0;
            Enqueue(new InputPacket(reference.SurfaceInstanceId, definition.inputAddressType, channel, number,
                value, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            return true;
        }

        internal MidiLedStyle GetEditorLedStyle(MidiControlReference reference)
        {
            var key = Key(reference.SurfaceInstanceId, reference.ControlId);
            return controls.ContainsKey(key) && !panicLatched ? ResolveLayer(key) : MidiLedStyle.Off;
        }
#endif

        public bool ValidateReference(MidiControlReference reference) => controls.ContainsKey(Key(reference.SurfaceInstanceId, reference.ControlId));

        public Observable<MidiControlState> Observe(MidiControlReference reference)
        {
            return Observable.Create<MidiControlState>(observer =>
            {
                observer.OnNext(GetState(reference));
                Action<MidiControlChange> callback = change => { if (Same(change.Reference, reference)) observer.OnNext(change.State); };
                ControlChanged += callback;
                return Disposable.Create(() => ControlChanged -= callback);
            });
        }

        public Observable<MidiControlChange> ObserveChanges(MidiControlReference reference)
        {
            return Observable.Create<MidiControlChange>(observer =>
            {
                Action<MidiControlChange> callback = change => { if (Same(change.Reference, reference)) observer.OnNext(change); };
                ControlChanged += callback;
                return Disposable.Create(() => ControlChanged -= callback);
            });
        }

        public void SetLed(MidiControlReference reference, string owner, MidiLedLayer layer, MidiLedStyle style)
        {
            var key = Key(reference.SurfaceInstanceId, reference.ControlId);
            if (!controls.TryGetValue(key, out var control))
            {
                services?.Report("MIDI_LED_CONTROL", $"LED control '{reference}' is not defined in its profile.", nodeId: reference.ControlId);
                return;
            }
            if (!control.Definition.hasLed)
            {
                services?.Report("MIDI_LED_CONTROL", $"MIDI control '{reference}' does not have an LED.", nodeId: reference.ControlId);
                return;
            }
            if (!layers.TryGetValue(key, out var values)) layers.Add(key, values = new Dictionary<string, LayerValue>(StringComparer.Ordinal));
            values[owner] = new LayerValue((int)layer, ++sequence, style);
        }

        public string CreateLayerOwner(MonoBehaviour owner)
        {
            var token = Guid.NewGuid().ToString("N");
            return token;
        }

        public void RemoveLayerOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return;
            foreach (var control in layers.Values) control.Remove(owner);
        }

        public void PanicAllOff() { panicLatched = true; sentPayloads.Clear(); }
        public void ResumeOutputs() { if (!panicLatched) return; panicLatched = false; sentPayloads.Clear(); foreach (var runtime in surfaceRuntime.Values) runtime.ForceFullResync = true; }

        private MidiLedStyle ResolveLayer(string key)
        {
            if (!layers.TryGetValue(key, out var values) || values.Count == 0) return MidiLedStyle.Off;
            LayerValue best = default;
            foreach (var value in values.Values)
                if (value.Priority > best.Priority || (value.Priority == best.Priority && value.Sequence > best.Sequence)) best = value;
            return best.Style;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not MidiDevice midiDevice) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled)
            {
                foreach (var runtime in surfaceRuntime.Values)
                {
                    if (!runtime.Devices.Contains(midiDevice)) continue;
                    DetachDevice(runtime, midiDevice);
                    runtime.Status = runtime.Devices.Count == 0 ? "Disconnected" : "Connected";
                }
                return;
            }
            ScanDevices();
        }

        private void ScanDevices()
        {
            if (!initialized) return;
            var instances = surfaceRuntime.Values.Select(surface => surface.Instance).ToArray();
            foreach (var runtime in surfaceRuntime.Values)
            {
                if (runtime.RawInput != null && !runtime.RawInput.IsOk) { runtime.RawInput.Dispose(); runtime.RawInput = null; runtime.Status = "Disconnected"; }
                if (runtime.Output == null && runtime.Instance.OutputPortOrdinal >= 0) TryOpenOutput(runtime);
                if (runtime.Output != null)
                {
                    if (runtime.Output.TakeError() is string error)
                        services?.Report("MIDI_OUTPUT", $"Shared MIDI output worker failed for {runtime.Instance.InstanceId}.", exception: new InvalidOperationException(error));
                    if (runtime.Output.ConnectionGeneration != runtime.OutputGeneration)
                    {
                        runtime.OutputGeneration = runtime.Output.ConnectionGeneration;
                        runtime.ForceFullResync = true;
                        RemoveSentPayloads(runtime);
                    }
                    if (!runtime.Output.IsOk) runtime.ForceFullResync = true;
                }
                if (runtime.RawInput == null && runtime.Instance.InputBackend == MidiInputBackend.RtMidiRaw) TryOpenRawInput(runtime);
                if (runtime.Instance.InputBackend != MidiInputBackend.Minis) continue;
                const string ambiguousStatus = "Ambiguous same-name MIDI product; use RtMidiRaw with explicit port ordinal";
                if (HasAmbiguousMinisAssignment(runtime.Instance, instances))
                {
                    if (runtime.Devices.Count > 0) DetachDevice(runtime);
                    if (!string.Equals(runtime.Status, ambiguousStatus, StringComparison.Ordinal))
                        services?.Report("MIDI_SURFACE_AMBIGUOUS", $"Surface '{runtime.Instance.InstanceId}' cannot identify '{runtime.Instance.ProductName}' by name because another Surface overlaps it.");
                    runtime.Status = ambiguousStatus;
                    continue;
                }
                var matches = InputSystem.devices.OfType<MidiDevice>().Where(device => ProductMatches(runtime.Instance.ProductName, DeviceProduct(device))).ToArray();
                foreach (var device in matches)
                {
                    if (runtime.Devices.Contains(device)) continue;
                    if (surfaceRuntime.Values.Any(other => other != runtime && other.Devices.Contains(device))) continue;
                    AttachDevice(runtime, device);
                }
                if (runtime.Devices.Count == 0) runtime.Status = "Disconnected";
            }
        }

        private static string DeviceProduct(MidiDevice device)
        {
            var product = device.description.product ?? string.Empty;
            var marker = product.LastIndexOf(" Channel ", StringComparison.Ordinal);
            return marker >= 0 ? product.Substring(0, marker) : product;
        }
        private static bool ProductMatches(string requested, string actual) => string.IsNullOrEmpty(requested) || (!string.IsNullOrEmpty(actual) && actual.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0);

        internal static bool HasAmbiguousMinisAssignment(MidiSurfaceInstance instance, IReadOnlyList<MidiSurfaceInstance> instances)
        {
            if (instance == null || instance.InputBackend != MidiInputBackend.Minis || instances == null) return false;
            for (var i = 0; i < instances.Count; i++)
            {
                var other = instances[i];
                if (other == null || ReferenceEquals(other, instance)) continue;
                if (ProductNamesOverlap(instance.ProductName, other.ProductName)) return true;
            }
            return false;
        }

        private static bool ProductNamesOverlap(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return true;
            return first.IndexOf(second, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   second.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AttachDevice(MidiSurfaceRuntime runtime, MidiDevice device)
        {
            runtime.Devices.Add(device);
            runtime.Status = "Connected";
            runtime.NoteOn = (note, velocity) => Enqueue(new InputPacket(runtime.Instance.InstanceId, MidiAddressType.Note, note.channel, note.noteNumber, velocity, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            runtime.NoteOff = note => Enqueue(new InputPacket(runtime.Instance.InstanceId, MidiAddressType.Note, note.channel, note.noteNumber, 0f, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            runtime.PolyAftertouch = (note, pressure) => Enqueue(new InputPacket(runtime.Instance.InstanceId, MidiAddressType.PolyAftertouch, note.channel, note.noteNumber, pressure, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            runtime.ControlChange = (control, value) => Enqueue(new InputPacket(runtime.Instance.InstanceId, MidiAddressType.ControlChange, control.channel, control.controlNumber, value, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            runtime.PitchBend = (control, value) => Enqueue(new InputPacket(runtime.Instance.InstanceId, MidiAddressType.PitchBend, device.channel, 0, value, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            runtime.Aftertouch = (control, value) => Enqueue(new InputPacket(runtime.Instance.InstanceId, MidiAddressType.ChannelPressure, device.channel, 0, value, 0, Stopwatch.GetTimestamp(), null, 0), clock: false);
            device.onWillNoteOn += runtime.NoteOn;
            device.onWillNoteOff += runtime.NoteOff;
            device.onWillAftertouch += runtime.PolyAftertouch;
            device.onWillControlChange += runtime.ControlChange;
            device.onWillPitchBend += runtime.PitchBend;
            device.onWillChannelPressure += runtime.Aftertouch;
            runtime.ForceFullResync = true;
        }

        private void DetachDevice(MidiSurfaceRuntime runtime, MidiDevice device = null)
        {
            var devices = device == null ? runtime.Devices.ToArray() : new[] { device };
            foreach (var current in devices)
            {
                current.onWillNoteOn -= runtime.NoteOn;
                current.onWillNoteOff -= runtime.NoteOff;
                current.onWillAftertouch -= runtime.PolyAftertouch;
                current.onWillControlChange -= runtime.ControlChange;
                current.onWillPitchBend -= runtime.PitchBend;
                current.onWillChannelPressure -= runtime.Aftertouch;
                runtime.Devices.Remove(current);
            }
        }

        private void TryOpenRawInput(MidiSurfaceRuntime runtime)
        {
            RtMidi.MidiIn input = null;
            try
            {
                runtime.RawInput?.Dispose();
                runtime.RawInput = null;
                input = RtMidi.MidiIn.Create();
                if (runtime.Instance.RawInputPortOrdinal < 0 || runtime.Instance.RawInputPortOrdinal >= input.PortCount)
                {
                    runtime.Status = "Waiting for Raw input port";
                    input.Dispose();
                    return;
                }
                input.IgnoreTypes(sysex: false, time: false, sense: true);
                input.MessageReceived += (time, data) => EnqueueRaw(runtime.Instance.InstanceId, time, data);
                input.OpenPort(runtime.Instance.RawInputPortOrdinal, "Mondmade VJ0 MIDI Input");
                runtime.RawInput = input;
                input = null;
                runtime.Status = "Connected";
            }
            catch (Exception exception) { input?.Dispose(); runtime.Status = "Disconnected"; services?.Report("MIDI_RAW_INPUT", $"Could not open raw MIDI input for {runtime.Instance.InstanceId}.", exception: exception); }
        }

        private void TryOpenOutput(MidiSurfaceRuntime runtime)
        {
            var output = MidiOutputDispatcher.Acquire(runtime.Instance.OutputPortOrdinal, "MidiHub " + runtime.Instance.InstanceId, out var error);
            if (output == null)
            {
                runtime.Status = "Waiting for MIDI output port";
                if (!string.IsNullOrEmpty(error))
                    services?.Report("MIDI_OUTPUT", $"Could not acquire MIDI output for {runtime.Instance.InstanceId}.", exception: new InvalidOperationException(error));
                return;
            }
            runtime.Output = output;
            runtime.OutputGeneration = output.ConnectionGeneration;
            runtime.ForceFullResync = true;
            RemoveSentPayloads(runtime);
            runtime.Status = output.Status;
        }

        private void EnqueueRaw(string surfaceId, double nativeTimestamp, ReadOnlySpan<byte> data)
        {
            if (data.Length == 0 || data.Length > SysExMaxLength) return;
            var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(buffer);
            var timestamp = Stopwatch.GetTimestamp();
            var packet = new InputPacket(surfaceId, default, 0, 0, 0f, 0, timestamp, buffer, data.Length, nativeTimestamp);
            var isClock = IsClockOrSysEx(buffer[0]);
            Enqueue(packet, isClock);
        }

        private static bool IsClockOrSysEx(byte status) => status == 0xF0 || status == 0xF2 || status >= 0xF8;

        private void Enqueue(InputPacket packet, bool clock)
        {
            lock (queueLock)
            {
                var queue = clock ? clockQueue : normalQueue;
                var capacity = clock ? ClockCapacity : NormalCapacity;
                var rawBufferBytes = packet.Raw?.Length ?? 0;
                if (queue.Count >= capacity || rawBufferBytes > RawQueueByteCapacity - queuedRawBytes)
                {
                    if (packet.Raw != null) ArrayPool<byte>.Shared.Return(packet.Raw);
                    if (clock) Interlocked.Increment(ref droppedClock); else Interlocked.Increment(ref droppedNormal);
                    return;
                }
                queue.Enqueue(packet);
                queuedRawBytes += rawBufferBytes;
            }
        }

        private void ProcessRaw(InputPacket packet)
        {
            var data = packet.Raw;
            var length = packet.Length;
            if (length == 0) return;
            var status = data[0];
            var timestamp = (double)packet.Timestamp / Stopwatch.Frequency;
            if (status == 0xF8) { if (services.TryGet<IExternalMidiClock>(out var clock)) clock.OnClockPulse(timestamp, packet.NativeTimestamp); return; }
            if (status == 0xFA) { if (services.TryGet<IExternalMidiClock>(out var clock)) clock.OnStart(timestamp); return; }
            if (status == 0xFB) { if (services.TryGet<IExternalMidiClock>(out var clock)) clock.OnContinue(timestamp); return; }
            if (status == 0xFC) { if (services.TryGet<IExternalMidiClock>(out var clock)) clock.OnStop(timestamp); return; }
            if (status == 0xF2 && length >= 3) { if (services.TryGet<IExternalMidiClock>(out var clock)) clock.OnSongPositionPointer(data[1] | data[2] << 7, timestamp); return; }
            if (status == 0xF0) return;
            if (length < 2) return;
            var kind = status & 0xF0;
            var channel = status & 0x0F;
            var number = data[1];
            var value = length > 2 ? data[2] / 127f : number / 127f;
            if (kind == 0xB0) ProcessControl(new InputPacket(packet.SurfaceId, MidiAddressType.ControlChange, channel, number, value, 0, packet.Timestamp, null, 0));
            else if (kind == 0x90) ProcessControl(new InputPacket(packet.SurfaceId, MidiAddressType.Note, channel, number, value, 0, packet.Timestamp, null, 0));
            else if (kind == 0x80) ProcessControl(new InputPacket(packet.SurfaceId, MidiAddressType.Note, channel, number, 0f, 0, packet.Timestamp, null, 0));
            else if (kind == 0xA0) ProcessControl(new InputPacket(packet.SurfaceId, MidiAddressType.PolyAftertouch, channel, number, value, 0, packet.Timestamp, null, 0));
            else if (kind == 0xE0) ProcessControl(new InputPacket(packet.SurfaceId, MidiAddressType.PitchBend, channel, 0, ((data[1] | data[2] << 7) - 8192) / 8192f, 0, packet.Timestamp, null, 0));
            else if (kind == 0xD0) ProcessControl(new InputPacket(packet.SurfaceId, MidiAddressType.ChannelPressure, channel, 0, number / 127f, 0, packet.Timestamp, null, 0));
        }

        private void ProcessControl(InputPacket packet)
        {
            if (!surfaceRuntime.TryGetValue(packet.SurfaceId, out var surface)) return;
            InvokeRawInputObserved(new MidiRawMessage(packet.SurfaceId, packet.AddressType, packet.Channel, packet.Number, packet.Value));
            foreach (var definition in surface.Instance.Profile.Controls)
            {
                if (definition == null || definition.inputAddressType != packet.AddressType || (definition.channel >= 0 && definition.channel != packet.Channel)) continue;
                if ((definition.inputAddressType is MidiAddressType.Note or MidiAddressType.ControlChange or MidiAddressType.PolyAftertouch) && definition.inputNumber != packet.Number) continue;
                var key = Key(packet.SurfaceId, definition.id);
                if (!controls.TryGetValue(key, out var runtime)) continue;
                var previous = runtime.State;
                var normalized = definition.inputAddressType == MidiAddressType.PitchBend
                    ? Mathf.Clamp(packet.Value, -1f, 1f)
                    : Mathf.Clamp01(packet.Value);
                var velocity = definition.inputAddressType == MidiAddressType.Note ? normalized : 0f;
                var pressed = definition.type is MidiControlType.Button or MidiControlType.VelocityButton ? normalized >= definition.pressThreshold && normalized > 0f : previous.Pressed;
                float value;
                if (definition.type == MidiControlType.RelativeEncoder)
                {
                    var raw = Mathf.Clamp(Mathf.RoundToInt(normalized * 127f), 0, 127);
                    var delta = DecodeRelative(raw, definition.relativeMode);
                    value = Mathf.Clamp(previous.Value + delta / 63f * (definition.maximum - definition.minimum), definition.minimum, definition.maximum);
                }
                else if (definition.type is MidiControlType.Button or MidiControlType.VelocityButton) value = pressed ? 1f : 0f;
                else if (definition.type == MidiControlType.PitchBend) value = normalized;
                else value = definition.minimum + normalized * (definition.maximum - definition.minimum);
                var pressure = definition.type == MidiControlType.Aftertouch ? Mathf.Clamp01(packet.Value) : previous.Pressure;
                var state = new MidiControlState(value, velocity, pressed, pressure, definition.groupIndex);
                runtime.State = state;
                var change = new MidiControlChange(new MidiControlReference(packet.SurfaceId, definition.id), state, !previous.Pressed && state.Pressed, previous.Pressed && !state.Pressed);
                services?.RecordMidiLogical(packet.SurfaceId, surface.Instance.ProductName, definition.id, definition.label, packet.Channel,
                    state.Value, state.Velocity, state.Pressure, state.Pressed, change.PressedDown, change.Released);
                InvokeControlChanged(change);
            }
        }

        private static int DecodeRelative(int value, MidiRelativeMode mode) => mode switch
        {
            MidiRelativeMode.BinaryOffset => value - 64,
            MidiRelativeMode.SignMagnitude => (value & 0x40) != 0 ? -(value & 0x3f) : value & 0x3f,
            _ => value > 63 ? value - 128 : value,
        };

        private void InvokeControlChanged(MidiControlChange change)
        {
            var callbacks = controlChangedCallbacks;
            for (var i = 0; i < callbacks.Length; i++)
            {
                try { callbacks[i](change); }
                catch (Exception exception) { services?.Report("MIDI_SUBSCRIBER", "A MIDI control subscriber failed.", nodeId: change.Reference.ControlId, exception: exception); }
            }
        }

        private void InvokeRawInputObserved(MidiRawMessage message)
        {
            var surfaceName = surfaceRuntime.TryGetValue(message.SurfaceInstanceId, out var surface) ? surface.Instance.ProductName : message.SurfaceInstanceId;
            services?.RecordMidiRaw(message.SurfaceInstanceId, surfaceName, (int)message.AddressType, message.Channel, message.Number, message.Value);
            var callbacks = rawInputCallbacks;
            for (var i = 0; i < callbacks.Length; i++)
            {
                try { callbacks[i](message); }
                catch (Exception exception) { services?.Report("MIDI_LEARN_SUBSCRIBER", "A MIDI learn subscriber failed.", exception: exception); }
            }
        }

        private static Action<T>[] AddCallback<T>(Action<T>[] callbacks, Action<T> callback)
        {
            if (callback == null) return callbacks;
            var result = new Action<T>[callbacks.Length + 1];
            Array.Copy(callbacks, result, callbacks.Length);
            result[result.Length - 1] = callback;
            return result;
        }

        private static Action<T>[] RemoveCallback<T>(Action<T>[] callbacks, Action<T> callback)
        {
            if (callback == null) return callbacks;
            for (var i = callbacks.Length - 1; i >= 0; i--)
                if (callbacks[i] == callback)
                {
                    var result = new Action<T>[callbacks.Length - 1];
                    if (i > 0) Array.Copy(callbacks, 0, result, 0, i);
                    if (i < callbacks.Length - 1) Array.Copy(callbacks, i + 1, result, i, callbacks.Length - i - 1);
                    return result;
                }
            return callbacks;
        }

        private void RemoveSentPayloads(MidiSurfaceRuntime runtime)
        {
            if (runtime.Instance.Profile == null) return;
            var prefix = runtime.Instance.InstanceId + "\0";
            foreach (var definition in runtime.Instance.Profile.Controls)
                if (definition != null) sentPayloads.Remove(prefix + definition.id);
        }

        private string GetOutputQueueKey(string controlKey)
        {
            if (!outputQueueKeys.TryGetValue(controlKey, out var queueKey))
            {
                queueKey = outputQueueOwner + (char)0 + controlKey;
                outputQueueKeys.Add(controlKey, queueKey);
            }
            return queueKey;
        }

        private static bool IsApcRgbPad(MidiSurfaceProfile profile, MidiSurfaceControl control)
            => string.Equals(profile.DriverId, "apc-mini-mk2", StringComparison.OrdinalIgnoreCase) &&
               control.outputAddressType == MidiAddressType.Note && control.outputNumber >= 0 && control.outputNumber < 64;

        private static ApcPadLedUpdate EncodeApcPadLed(string key, MidiLedPayload payload)
        {
            MidiLedEncoder.EncodeApcPadRgb(payload, out var red, out var green, out var blue);
            return new ApcPadLedUpdate(key, payload.Number, red, green, blue);
        }

        private void ReleaseSurface(MidiSurfaceRuntime runtime, bool sendAllOff)
        {
            DetachDevice(runtime);
            if (sendAllOff && runtime.Output != null)
            {
                foreach (var definition in runtime.Instance.Profile.Controls)
                {
                    if (definition == null || !definition.hasLed) continue;
                    try
                    {
                        var payload = MidiLedEncoder.Encode(runtime.Instance.Profile.DriverId, definition, MidiLedStyle.Off);
                        var key = Key(runtime.Instance.InstanceId, definition.id);
                        runtime.Output.QueueLed(GetOutputQueueKey(key), payload.Status, payload.Number, payload.Value);
                    }
                    catch { }
                }
            }
            runtime.RawInput?.Dispose(); runtime.RawInput = null;
            CloseOutput(runtime);
        }
        private static void CloseOutput(MidiSurfaceRuntime runtime) { runtime.Output?.Dispose(); runtime.Output = null; }
        private static void ReturnRaw(InputPacket packet) { if (packet.Raw != null) ArrayPool<byte>.Shared.Return(packet.Raw); }
        private void ReturnQueuedRaw(InputPacket packet)
        {
            if (packet.Raw == null) return;
            ReturnRaw(packet);
            queuedRawBytes -= packet.Raw.Length;
        }
        private static string Key(string surface, string control) => (surface ?? string.Empty) + "\0" + (control ?? string.Empty);
        private static bool Same(MidiControlReference a, MidiControlReference b) => string.Equals(a.SurfaceInstanceId, b.SurfaceInstanceId, StringComparison.Ordinal) && string.Equals(a.ControlId, b.ControlId, StringComparison.Ordinal);

        private sealed class ControlRuntime { public readonly MidiSurfaceControl Definition; public readonly string SurfaceId; public readonly string Key; public MidiControlState State; public ControlRuntime(MidiSurfaceControl d, string surface, string key) { Definition=d; SurfaceId=surface; Key=key; State=new MidiControlState(d.initialValue,0,false,0,d.groupIndex); } }
        private sealed class MidiSurfaceRuntime
        {
            public readonly MidiSurfaceInstance Instance; public readonly Dictionary<string, ControlRuntime> ControlsById = new Dictionary<string, ControlRuntime>(StringComparer.Ordinal); public string Status = "Disconnected"; public readonly HashSet<MidiDevice> Devices = new HashSet<MidiDevice>(); public RtMidi.MidiIn RawInput; public MidiOutputLease Output; public long OutputGeneration; public bool ForceFullResync;
            public readonly List<ApcPadLedUpdate> PendingApcPadUpdates = new List<ApcPadLedUpdate>(64);
            public readonly List<PendingApcPadState> PendingApcPadStates = new List<PendingApcPadState>(64);
            public Action<MidiNoteControl,float> NoteOn; public Action<MidiNoteControl> NoteOff; public Action<MidiNoteControl,float> PolyAftertouch; public Action<MidiValueControl,float> ControlChange; public Action<UnityEngine.InputSystem.Controls.AxisControl,float> PitchBend, Aftertouch;
            public MidiSurfaceRuntime(MidiSurfaceInstance instance) => Instance=instance;
        }
        private readonly struct InputPacket
        {
            public readonly string SurfaceId; public readonly MidiAddressType AddressType; public readonly int Channel, Number; public readonly float Value; public readonly int Velocity; public readonly long Timestamp; public readonly byte[] Raw; public readonly int Length; public readonly double NativeTimestamp;
            public InputPacket(string surface, MidiAddressType kind, int channel, int number, float value, int velocity, long timestamp, byte[] raw, int length, double nativeTimestamp=0d) { SurfaceId=surface; AddressType=kind; Channel=channel; Number=number; Value=value; Velocity=velocity; Timestamp=timestamp; Raw=raw; Length=length; NativeTimestamp=nativeTimestamp; }
        }
        private readonly struct LayerValue { public readonly int Priority; public readonly long Sequence; public readonly MidiLedStyle Style; public LayerValue(int priority,long sequence,MidiLedStyle style){Priority=priority;Sequence=sequence;Style=style;} }
        private readonly struct PendingApcPadState
        {
            public readonly string ControlKey;
            public readonly MidiSurfaceControl Control;
            public readonly MidiLedStyle Style;
            public readonly MidiLedPayload Payload;
            public PendingApcPadState(string controlKey, MidiSurfaceControl control, MidiLedStyle style, MidiLedPayload payload)
            {
                ControlKey = controlKey;
                Control = control;
                Style = style;
                Payload = payload;
            }
        }
    }

    internal readonly struct MidiLedPayload : IEquatable<MidiLedPayload>
    {
        public readonly byte Status, Number, Value;
        public MidiLedPayload(byte status, byte number, byte value) { Status = status; Number = number; Value = value; }
        public bool Equals(MidiLedPayload other) => Status == other.Status && Number == other.Number && Value == other.Value;
        public override bool Equals(object obj) => obj is MidiLedPayload payload && Equals(payload);
        public override int GetHashCode() => Status * 397 ^ Number * 31 ^ Value;
    }

    internal static class MidiLedEncoder
    {
        private static readonly float[] apcBrightnessLevels = { 0.10f, 0.25f, 0.50f, 0.65f, 0.75f, 0.90f, 1f };
        private static readonly float[] apcBlinkRates = { 1f / 24f, 1f / 12f, 1f / 8f, 1f / 4f, 1f / 2f };
        private static readonly float[] apcPulseRates = { 1f / 16f, 1f / 8f, 1f / 4f, 1f / 2f };
        private static readonly string[] apcPaletteHex = { "000000","1E1E1E","7F7F7F","FFFFFF","FF4C4C","FF0000","590000","190000","FFBD6C","FF5400","591D00","271B00","FFFF4C","FFFF00","595900","191900","88FF4C","54FF00","1D5900","142B00","4CFF4C","00FF00","005900","001900","4CFF5E","00FF19","00590D","001902","4CFF88","00FF55","00591D","001F12","4CFFB7","00FF99","005935","001912","4CC3FF","00A9FF","004152","001019","4C88FF","0055FF","001D59","000819","4C4CFF","0000FF","000059","000019","874CFF","5400FF","190064","0F0030","FF4CFF","FF00FF","590059","190019","FF4C87","FF0054","59001D","220013","FF1500","993500","795100","436400","033900","005735","00547F","0000FF","00454F","2500CC","7F7F7F","202020","FF0000","BDFF2D","AFED06","64FF09","108B00","00FF87","00A9FF","002AFF","3F00FF","7A00FF","B21A7D","402100","FF4A00","88E106","72FF15","00FF00","3BFF26","59FF71","38FFCC","5B8AFF","3151C6","877FE9","D31DFF","FF005D","FF7F00","B9B000","90FF00","835D07","392B00","144C10","0D5038","15152A","16205A","693C1C","A8000A","DE513D","D86A1C","FFE126","9EE12F","67B50F","1E1E30","DCFF6B","80FFBD","9A99FF","8E66FF","404040","757575","E0FFFF","A00000","350000","1AD000","074200","B9B000","3F3100","B35F00","4B1502" };
        private static readonly Color[] apcPalette = BuildPalette();
        private static readonly Color[] genericPalette = { Color.black, Color.white, Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, new Color(1,0.5f,0), new Color(0.5f,0,1), new Color(0,0.5f,1), new Color(0,1,0.5f), new Color(1,0,0.5f), new Color(0.5f,1,0), new Color(1,1,0.5f), new Color(0.5f,1,1f) };
        public static MidiLedPayload Encode(string driverId, MidiSurfaceControl control, MidiLedStyle style)
        {
            var apc = string.Equals(driverId, "apc-mini-mk2", StringComparison.OrdinalIgnoreCase);
            var isApcSingle = apc && control.outputAddressType == MidiAddressType.Note && control.outputNumber >= 0x64 && control.outputNumber <= 0x77;
            var color = style.color;
            var code = 0;
            var distance = float.PositiveInfinity;
            var palette = apc ? apcPalette : genericPalette;
            for (var i=0;i<palette.Length;i++)
            {
                var candidate = apc ? palette[i].linear : palette[i];
                var sample = apc ? color.linear : color;
                var d=(sample.r-candidate.r)*(sample.r-candidate.r)+(sample.g-candidate.g)*(sample.g-candidate.g)+(sample.b-candidate.b)*(sample.b-candidate.b);
                if(d<distance){distance=d;code=i;}
            }
            int channel;
            int velocity;
            if (isApcSingle)
            {
                channel = 0;
                velocity = style.brightness <= 0f ? 0 : style.effect == MidiLedEffect.Blink ? 2 : 1;
            }
            else if (apc && control.outputAddressType == MidiAddressType.Note && control.outputNumber < 64)
            {
                channel = style.effect switch
                {
                    MidiLedEffect.Blink => 11 + NearestApcRate(style.durationSeconds, apcBlinkRates),
                    MidiLedEffect.Pulse => 7 + NearestApcRate(style.durationSeconds, apcPulseRates),
                    _ => NearestApcBrightness(style.brightness)
                };
                velocity = style.brightness <= 0f ? 0 : code;
            }
            else
            {
                channel = control.outputChannel & 0x0F;
                velocity = style.brightness <= 0f ? 0 : code;
            }
            var status = (byte)((control.outputAddressType == MidiAddressType.ControlChange ? 0xB0 : 0x90) | (channel & 0x0F));
            return new MidiLedPayload(status, (byte)Mathf.Clamp(control.outputNumber,0,127), (byte)Mathf.Clamp(velocity,0,127));
        }

        public static void EncodeApcPadRgb(MidiLedPayload payload, out byte red, out byte green, out byte blue)
        {
            red = green = blue = 0;
            if (payload.Value == 0) return;
            var color = apcPalette[payload.Value];
            var brightnessChannel = payload.Status & 0x0F;
            var brightness = brightnessChannel < apcBrightnessLevels.Length ? apcBrightnessLevels[brightnessChannel] : 1f;
            red = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f * brightness), 0, 255);
            green = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f * brightness), 0, 255);
            blue = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f * brightness), 0, 255);
        }

        private static Color[] BuildPalette()
        {
            var result = new Color[apcPaletteHex.Length];
            for (var i=0;i<result.Length;i++)
            {
                ColorUtility.TryParseHtmlString("#" + apcPaletteHex[i], out result[i]);
            }
            return result;
        }
        private static int NearestApcBrightness(float value)
        {
            var selected = 0; var distance = float.PositiveInfinity;
            for(var i=0;i<apcBrightnessLevels.Length;i++){var next=Mathf.Abs(value-apcBrightnessLevels[i]);if(next<distance){distance=next;selected=i;}}
            return selected;
        }
        private static int NearestApcRate(float value, float[] rates)
        {
            if (value <= 0f) return rates.Length - 1;
            var selected = 0;
            var distance = float.PositiveInfinity;
            for (var i = 0; i < rates.Length; i++)
            {
                var next = Mathf.Abs(value - rates[i]);
                if (next < distance) { distance = next; selected = i; }
            }
            return selected;
        }
    }
}
