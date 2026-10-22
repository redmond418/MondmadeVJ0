using System;

namespace MondmadeVJ0
{
    public enum RuntimeTraceStage { SourceRaw, Modifier, Final, Output, TriggerRaw, TriggerModifier, TriggerOutput }
    public enum RuntimeMidiEventKind { RawInput, LogicalInput, LedOutputQueued }
    public enum RuntimeTransportEventKind { Tap, MidiClockPulse, MidiClockStart, MidiClockContinue, MidiClockStop, SongPosition, Seek }

    public sealed class RouteTraceSelection
    {
        internal volatile bool IsSelected;
        internal long LastStageTimestampTicks;
        internal long LastStageFrameIndex = -1L;
        internal RouteTraceSelection(string routeId) => RouteId = routeId;
        public string RouteId { get; }
    }

    public readonly struct RuntimeRouteTrace
    {
        public readonly long Sequence;
        public readonly double Timestamp;
        public readonly string RouteId;
        public readonly string RouteName;
        public readonly RuntimeTraceStage Stage;
        public readonly int Index;
        public readonly string NodeId;
        public readonly string NodeName;
        public readonly object Value;
        public readonly string Outcome;
        public readonly double ProcessingMilliseconds;

        public RuntimeRouteTrace(long sequence, double timestamp, string routeId, string routeName,
            RuntimeTraceStage stage, int index, string nodeId, string nodeName, object value, string outcome, double processingMilliseconds)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            RouteId = routeId;
            RouteName = routeName;
            Stage = stage;
            Index = index;
            NodeId = nodeId;
            NodeName = nodeName;
            Value = value;
            Outcome = outcome;
            ProcessingMilliseconds = processingMilliseconds;
        }
    }

    public readonly struct RuntimeMidiTrace
    {
        public readonly long Sequence;
        public readonly double Timestamp;
        public readonly RuntimeMidiEventKind Kind;
        public readonly string SurfaceId;
        public readonly string SurfaceName;
        public readonly string ControlId;
        public readonly string Label;
        public readonly int Channel;
        public readonly int AddressType;
        public readonly int Number;
        public readonly float Value;
        public readonly float Velocity;
        public readonly float Pressure;
        public readonly bool Pressed;
        public readonly bool PressedDown;
        public readonly bool Released;
        public readonly int Red;
        public readonly int Green;
        public readonly int Blue;
        public readonly float Brightness;
        public readonly int Effect;

        public RuntimeMidiTrace(long sequence, double timestamp, RuntimeMidiEventKind kind, string surfaceId,
            string surfaceName, string controlId, string label, int channel, int addressType, int number, float value, float velocity = 0f,
            float pressure = 0f, bool pressed = false, bool pressedDown = false, bool released = false,
            int red = 0, int green = 0, int blue = 0, float brightness = 0f, int effect = 0)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            Kind = kind;
            SurfaceId = surfaceId;
            SurfaceName = surfaceName;
            ControlId = controlId;
            Label = label;
            Channel = channel;
            AddressType = addressType;
            Number = number;
            Value = value;
            Velocity = velocity;
            Pressure = pressure;
            Pressed = pressed;
            PressedDown = pressedDown;
            Released = released;
            Red = red;
            Green = green;
            Blue = blue;
            Brightness = brightness;
            Effect = effect;
        }
    }

    public readonly struct RuntimeTransportTrace
    {
        public readonly long Sequence;
        public readonly double Timestamp;
        public readonly RuntimeTransportEventKind Kind;
        public readonly double Bpm;
        public readonly double BeatPosition;
        public readonly string Detail;

        public RuntimeTransportTrace(long sequence, double timestamp, RuntimeTransportEventKind kind,
            double bpm, double beatPosition, string detail)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            Kind = kind;
            Bpm = bpm;
            BeatPosition = beatPosition;
            Detail = detail;
        }
    }
}
