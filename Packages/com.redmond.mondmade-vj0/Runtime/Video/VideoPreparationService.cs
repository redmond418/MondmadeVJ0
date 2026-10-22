using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Video;

namespace MondmadeVJ0
{
    internal enum VideoPreparationLane { Value, Trigger }

    internal static class VideoPreparationServices
    {
        public static VideoPreparationService Get(RouteContext context)
        {
            if (context.Services.TryGet<VideoPreparationService>(out var service)) return service;
            service = new VideoPreparationService();
            context.Services.Register(service);
            return service;
        }
    }

    internal sealed class VideoPreparationService
    {
        private readonly Dictionary<VideoPlayer, VideoPreparationState> states =
            new Dictionary<VideoPlayer, VideoPreparationState>(VideoPlayerReferenceComparer.Instance);
        private readonly Action<VideoPlayer> requestPrepare;

        public VideoPreparationService(Action<VideoPlayer> requestPrepare = null)
            => this.requestPrepare = requestPrepare ?? (player => player.Prepare());

        public VideoPendingOperation Acquire(VideoPlayer player, RouteContext context, RouteNode node, VideoPreparationLane lane)
        {
            if (player == null) throw new InvalidOperationException("VideoPlayer target is not assigned.");
            if (!states.TryGetValue(player, out var state))
            {
                state = new VideoPreparationState(player, context.FrameClock, requestPrepare);
                states.Add(player, state);
            }
            return new VideoPendingOperation(state, lane, node, () => Release(player, state));
        }

        private void Release(VideoPlayer player, VideoPreparationState state)
        {
            if (state.ReferenceCount != 0) return;
            if (states.TryGetValue(player, out var current) && ReferenceEquals(current, state)) states.Remove(player);
            state.Dispose();
        }

        private sealed class VideoPlayerReferenceComparer : IEqualityComparer<VideoPlayer>
        {
            public static readonly VideoPlayerReferenceComparer Instance = new VideoPlayerReferenceComparer();
            public bool Equals(VideoPlayer x, VideoPlayer y) => ReferenceEquals(x, y);
            public int GetHashCode(VideoPlayer obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }

    internal sealed class VideoPendingOperation : IDisposable
    {
        public const double PrepareTimeoutSeconds = 10d;

        private readonly VideoPreparationState state;
        private readonly VideoPreparationLane lane;
        private readonly RouteNode node;
        private readonly Action releaseFromService;
        private readonly bool ownsState;
        private bool disposed;
        private bool faulted;

        public event Action<string, string, Exception> Faulted;
        public bool IsPreparing => state.IsPreparing;
        public bool IsFaulted => faulted;

        // Standalone constructor used by contract tests; runtime Outputs acquire through the shared service.
        public VideoPendingOperation(VideoPlayer target, RouteContext context, RouteNode node,
            Action<VideoPlayer> requestPrepare = null)
        {
            this.node = node;
            lane = VideoPreparationLane.Value;
            state = new VideoPreparationState(target, context.FrameClock, requestPrepare ?? (player => player.Prepare()));
            ownsState = true;
            state.Attach(this);
        }

        internal VideoPendingOperation(VideoPreparationState state, VideoPreparationLane lane, RouteNode node, Action releaseFromService)
        {
            this.state = state;
            this.lane = lane;
            this.node = node;
            this.releaseFromService = releaseFromService;
            state.Attach(this);
        }

        public void Submit(Action command, bool reprepare = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VideoPendingOperation));
            if (faulted) return;
            state.Submit(this, lane, command, reprepare);
        }

        internal void HandlePrepared(VideoPlayer player) => state.HandlePrepared(player);
        internal void HandleError(VideoPlayer player, string message) => state.HandleError(player, message);

        internal void ReportFault(string code, string message, Exception exception)
        {
            if (disposed || faulted) return;
            faulted = true;
            Faulted?.Invoke(code, message, exception);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            state.Detach(this);
            Faulted = null;
            if (ownsState) state.Dispose();
            else releaseFromService?.Invoke();
        }
    }

    internal sealed class VideoPreparationState : IDisposable
    {
        private readonly VideoPlayer target;
        private readonly IFrameClock clock;
        private readonly Action<VideoPlayer> requestPrepare;
        private readonly HashSet<VideoPendingOperation> operations = new HashSet<VideoPendingOperation>();
        private readonly List<PendingValue> pendingValues = new List<PendingValue>();
        private VideoPendingOperation pendingTriggerOwner;
        private Action pendingTrigger;
        private long sequence;
        private bool disposed;
        private double deadline;

        public bool IsPreparing { get; private set; }
        public int ReferenceCount => operations.Count;

        public VideoPreparationState(VideoPlayer target, IFrameClock clock, Action<VideoPlayer> requestPrepare)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.requestPrepare = requestPrepare ?? throw new ArgumentNullException(nameof(requestPrepare));
            target.prepareCompleted += OnPrepared;
            target.errorReceived += OnError;
            clock.Updated += OnFrameUpdated;
        }

        public void Attach(VideoPendingOperation operation) => operations.Add(operation);

        public void Detach(VideoPendingOperation operation)
        {
            operations.Remove(operation);
            for (var i = pendingValues.Count - 1; i >= 0; i--)
                if (ReferenceEquals(pendingValues[i].Owner, operation)) pendingValues.RemoveAt(i);
            if (ReferenceEquals(pendingTriggerOwner, operation))
            {
                pendingTriggerOwner = null;
                pendingTrigger = null;
            }
        }

        public void Submit(VideoPendingOperation operation, VideoPreparationLane lane, Action command, bool reprepare)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VideoPreparationState));
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (lane == VideoPreparationLane.Value)
            {
                for (var i = pendingValues.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(pendingValues[i].Owner, operation)) pendingValues.RemoveAt(i);
                pendingValues.Add(new PendingValue(operation, command, ++sequence));
            }
            else
            {
                pendingTriggerOwner = operation;
                pendingTrigger = command;
            }

            if (reprepare)
            {
                IsPreparing = false;
                try { target.Stop(); }
                catch (Exception exception)
                {
                    FailPending("VIDEO_PREPARE_FAILED", "VideoPlayer could not stop before restarting.", exception);
                    return;
                }
                BeginPrepare();
                return;
            }

            if (target.isPrepared && !IsPreparing)
            {
                if (lane == VideoPreparationLane.Value)
                {
                    pendingValues.RemoveAll(item => ReferenceEquals(item.Owner, operation));
                }
                else if (ReferenceEquals(pendingTriggerOwner, operation))
                {
                    pendingTriggerOwner = null;
                    pendingTrigger = null;
                }
                command();
                return;
            }

            if (!IsPreparing) BeginPrepare();
        }

        public void HandlePrepared(VideoPlayer player)
        {
            if (disposed || player != target) return;
            IsPreparing = false;
            var values = pendingValues.ToArray();
            pendingValues.Clear();
            Array.Sort(values, (left, right) => left.Sequence.CompareTo(right.Sequence));
            var triggerOwner = pendingTriggerOwner;
            var trigger = pendingTrigger;
            pendingTriggerOwner = null;
            pendingTrigger = null;

            for (var i = 0; i < values.Length; i++) Apply(values[i].Owner, values[i].Command);
            if (trigger != null) Apply(triggerOwner, trigger);
        }

        public void HandleError(VideoPlayer player, string message)
        {
            if (disposed || player != target || !HasPending) return;
            var detail = string.IsNullOrWhiteSpace(message) ? "VideoPlayer preparation failed." : message;
            FailPending("VIDEO_PREPARE_FAILED", detail, new InvalidOperationException(detail));
        }

        private bool HasPending => pendingValues.Count > 0 || pendingTrigger != null;

        private void BeginPrepare()
        {
            if (disposed) return;
            IsPreparing = true;
            deadline = clock.UnscaledTime + VideoPendingOperation.PrepareTimeoutSeconds;
            try { requestPrepare(target); }
            catch (Exception exception) { FailPending("VIDEO_PREPARE_FAILED", "VideoPlayer.Prepare failed.", exception); }
        }

        private void Apply(VideoPendingOperation owner, Action command)
        {
            if (owner == null) return;
            try { command(); }
            catch (Exception exception)
            {
                owner.ReportFault("VIDEO_PREPARE_APPLY", "A pending VideoPlayer Output could not be applied after preparation.", exception);
            }
        }

        private void OnFrameUpdated(double deltaTime, double unscaledTime)
        {
            if (disposed || !IsPreparing) return;
            if (target == null)
            {
                FailPending("VIDEO_PREPARE_FAILED", "VideoPlayer was destroyed while preparing.", new MissingReferenceException("VideoPlayer target was destroyed."));
                return;
            }
            if (unscaledTime >= deadline)
            {
                try { target.Stop(); }
                catch { }
                FailPending("VIDEO_PREPARE_TIMEOUT", $"VideoPlayer did not prepare within {VideoPendingOperation.PrepareTimeoutSeconds:0} seconds.",
                    new TimeoutException("VideoPlayer preparation timed out."));
            }
        }

        private void FailPending(string code, string message, Exception exception)
        {
            IsPreparing = false;
            var failed = new HashSet<VideoPendingOperation>();
            foreach (var value in pendingValues) if (value.Owner != null) failed.Add(value.Owner);
            if (pendingTriggerOwner != null) failed.Add(pendingTriggerOwner);
            pendingValues.Clear();
            pendingTriggerOwner = null;
            pendingTrigger = null;
            foreach (var operation in failed) operation.ReportFault(code, message, exception);
        }

        private void OnPrepared(VideoPlayer player) => HandlePrepared(player);
        private void OnError(VideoPlayer player, string message) => HandleError(player, message);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (target != null)
            {
                target.prepareCompleted -= OnPrepared;
                target.errorReceived -= OnError;
            }
            clock.Updated -= OnFrameUpdated;
            IsPreparing = false;
            pendingValues.Clear();
            pendingTriggerOwner = null;
            pendingTrigger = null;
            operations.Clear();
        }

        private readonly struct PendingValue
        {
            public readonly VideoPendingOperation Owner;
            public readonly Action Command;
            public readonly long Sequence;
            public PendingValue(VideoPendingOperation owner, Action command, long sequence)
            { Owner = owner; Command = command; Sequence = sequence; }
        }
    }
}
