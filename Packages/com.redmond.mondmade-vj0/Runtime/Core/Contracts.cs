using System;
using R3;
using UnityEngine;

namespace MondmadeVJ0
{
    public interface IValueSource<T> { Observable<T> Build(RouteContext context); }
    public interface IValueModifier<T> { Observable<T> Apply(Observable<T> source, RouteContext context); }
    public interface IValueOutput<T>
    {
        void Initialize(RouteContext context);
        void Write(T value);
    }

    public interface ITriggerSource { Observable<Unit> Build(RouteContext context); }
    public interface ITriggerModifier { Observable<Unit> Apply(Observable<Unit> source, RouteContext context); }
    public interface ITriggerOutput
    {
        void Initialize(RouteContext context);
        void Invoke();
    }

    public interface IRoute
    {
        string Id { get; }
        string Name { get; }
        bool IsEnabled { get; }
        string Status { get; }
        Type ValueType { get; }
        void SetEnabled(bool enabled);
        void Bind(MondmadeVJ0Runtime runtime, RouteHost host, RuntimeStateStore stateStore);
        void Unbind();
    }

    public interface IRuntimeModule
    {
        string ModuleName { get; }
        void Register(RuntimeServices services);
        void Initialize(RuntimeServices services);
        void DrainInputQueue(RuntimeServices services, int maxEvents);
        void Tick(RuntimeServices services, double deltaTime);
        void FlushFrame(RuntimeServices services);
        void Shutdown(RuntimeServices services);
    }

    public interface IFrameClock
    {
        long FrameIndex { get; }
        double Time { get; }
        double UnscaledTime { get; }
        double DspTime { get; }
        double DeltaTime { get; }
        double UnscaledDeltaTime { get; }
        /// <summary>Raised after each module tick with unscaled delta time and unscaled absolute time.</summary>
        event Action<double, double> Updated;
    }

    public interface IMusicalTransport
    {
        double BaseBpm { get; }
        double Multiplier { get; }
        double EffectiveBpm { get; }
        double BeatPosition { get; }
        long BeatIndex { get; }
        double BeatPhase { get; }
        long BarIndex { get; }
        double BarPhase { get; }
        double BarLengthBeats { get; }
        /// <summary>Increases when timeline position or external-clock continuity changes.</summary>
        long TimelineRevision { get; }
        bool IsRunning { get; }
        event Action<long> Beat;
        event Action<long> Bar;
        void SetBaseBpm(double bpm);
        void SetMultiplier(double multiplier);
        void Tap(double timestamp);
        void ResetPhase();
        void Nudge(double beats);
    }

    public interface IExternalMidiClock
    {
        void OnClockPulse(double monotonicTimestamp, double nativeTimestamp);
        void OnStart(double monotonicTimestamp);
        void OnContinue(double monotonicTimestamp);
        void OnStop(double monotonicTimestamp);
        void OnSongPositionPointer(int sixteenthNotes, double monotonicTimestamp);
    }

    /// <summary>Receives absolute beat-position updates from an external transport source.</summary>
    public interface IExternalTransportClock
    {
        void SetExternalClock(double bpm, double beatPosition, bool running, double monotonicTimestamp);
    }

    [Serializable]
    public abstract class RouteNode
    {
        [SerializeField] private string nodeId = Guid.NewGuid().ToString("N");
        public string NodeId => nodeId;

        public void RegenerateId() => nodeId = Guid.NewGuid().ToString("N");
    }
}
