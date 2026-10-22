using System;
using UnityEngine;
using UnityEngine.Video;

namespace MondmadeVJ0
{
    public enum VideoClipSwitchMode { KeepState, AutoPlay, Restart }

    internal static class VideoOutputHelpers
    {
        public static void Require(VideoPlayer player)
        {
            if (player == null) throw new InvalidOperationException("VideoPlayer target is not assigned.");
        }

        public static void RequireTime(VideoPlayer player)
        {
            Require(player);
            if (!player.canSetTime)
                throw new InvalidOperationException("VideoPlayer cannot set its time (canSetTime is false); this Output is disabled.");
        }

        public static double ClampTime(VideoPlayer player, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            var length = player.length;
            return Math.Max(0d, length > 0d ? Math.Min(value, length) : value);
        }

        public static float ClampNormalized(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }

    [Serializable]
    public abstract class VideoPlayerOutputBase : RouteNode, IAsyncRouteOutputFaultSource
    {
        [SerializeField] protected VideoPlayer target;
        [NonSerialized] private VideoPendingOperation pendingOperation;

        public event Action<string, string, Exception> Faulted;

        protected void RequireTarget() => VideoOutputHelpers.Require(target);

        private protected void InitializePendingOperation(RouteContext context, VideoPreparationLane lane)
        {
            RequireTarget();
            pendingOperation?.Dispose();
            var operation = VideoPreparationServices.Get(context).Acquire(target, context, this, lane);
            operation.Faulted += OnPendingOperationFaulted;
            pendingOperation = operation;
            context.Scope.Add(() =>
            {
                operation.Faulted -= OnPendingOperationFaulted;
                operation.Dispose();
                if (ReferenceEquals(pendingOperation, operation)) pendingOperation = null;
            });
        }

        protected void SubmitOrDefer(Action command, bool reprepare = false)
        {
            if (pendingOperation == null) throw new InvalidOperationException("VideoPlayer Output is not initialized.");
            pendingOperation.Submit(command, reprepare);
        }

        private void OnPendingOperationFaulted(string code, string message, Exception exception)
            => Faulted?.Invoke(code, message, exception);
    }

    [Serializable]
    public sealed class VideoPlayerSpeedFloatOutput : VideoPlayerOutputBase, IValueOutput<float>
    {
        public void Initialize(RouteContext context)
        {
            RequireTarget();
            if (!target.canSetPlaybackSpeed)
                throw new InvalidOperationException("VideoPlayer cannot set playback speed (canSetPlaybackSpeed is false); this Output is disabled.");
            InitializePendingOperation(context, VideoPreparationLane.Value);
        }

        public void Write(float value) => SubmitOrDefer(() => target.playbackSpeed = value);
    }

    [Serializable]
    public sealed class VideoPlayerPlayBoolOutput : VideoPlayerOutputBase, IValueOutput<bool>
    {
        public void Initialize(RouteContext context) => InitializePendingOperation(context, VideoPreparationLane.Value);
        public void Write(bool value) => SubmitOrDefer(() => { if (value) target.Play(); else target.Pause(); });
    }

    [Serializable]
    public sealed class VideoPlayerTimeFloatOutput : VideoPlayerOutputBase, IValueOutput<float>
    {
        public void Initialize(RouteContext context)
        {
            VideoOutputHelpers.RequireTime(target);
            InitializePendingOperation(context, VideoPreparationLane.Value);
        }

        public void Write(float value)
        {
            var time = VideoOutputHelpers.ClampTime(target, value);
            SubmitOrDefer(() =>
            {
                if (!target.canSetTime) throw new InvalidOperationException("VideoPlayer can no longer set time.");
                target.time = VideoOutputHelpers.ClampTime(target, time);
            });
        }
    }

    [Serializable]
    public sealed class VideoPlayerNormalizedTimeFloatOutput : VideoPlayerOutputBase, IValueOutput<float>
    {
        public void Initialize(RouteContext context)
        {
            VideoOutputHelpers.RequireTime(target);
            InitializePendingOperation(context, VideoPreparationLane.Value);
        }

        public void Write(float value)
        {
            var normalized = VideoOutputHelpers.ClampNormalized(value);
            SubmitOrDefer(() =>
            {
                if (!target.canSetTime) throw new InvalidOperationException("VideoPlayer can no longer set time.");
                target.time = target.length * normalized;
            });
        }
    }

    [Serializable]
    public sealed class VideoPlayerClipIndexIntOutput : VideoPlayerOutputBase, IValueOutput<int>
    {
        [SerializeField] private VideoClip[] clips = Array.Empty<VideoClip>();
        [SerializeField] private VideoClipSwitchMode switchMode = VideoClipSwitchMode.KeepState;
        [NonSerialized] private int currentIndex = -1;
        [NonSerialized] private bool hasPendingSwitch;
        [NonSerialized] private bool pendingWasPlaying;
        [NonSerialized] private bool pendingWasPaused;
        [NonSerialized] private double pendingPreviousTime;

        public void Initialize(RouteContext context)
        {
            RequireTarget();
            if (clips == null || clips.Length == 0) throw new InvalidOperationException("VideoPlayer clip list is empty.");
            for (var i = 0; i < clips.Length; i++) if (clips[i] == null) throw new InvalidOperationException($"VideoPlayer clip {i} is not assigned.");
            if (switchMode == VideoClipSwitchMode.KeepState && !target.canSetTime)
                throw new InvalidOperationException("KeepState requires a seekable VideoPlayer (canSetTime is false); this Output is disabled.");
            currentIndex = -1;
            hasPendingSwitch = false;
            InitializePendingOperation(context, VideoPreparationLane.Value);
        }

        public void Write(int value)
        {
            var index = Mathf.Clamp(value, 0, clips.Length - 1);
            if (switchMode != VideoClipSwitchMode.Restart && index == currentIndex && target.clip == clips[index]) return;
            if (!hasPendingSwitch)
            {
                pendingWasPlaying = target.isPlaying;
                pendingWasPaused = target.isPaused;
                pendingPreviousTime = target.time;
                hasPendingSwitch = true;
            }
            currentIndex = index;
            target.clip = clips[index];

            if (switchMode == VideoClipSwitchMode.KeepState)
            {
                SubmitOrDefer(() =>
                {
                    if (!target.canSetTime) throw new InvalidOperationException("VideoPlayer can no longer set time required by KeepState.");
                    target.time = VideoOutputHelpers.ClampTime(target, pendingPreviousTime);
                    if (pendingWasPlaying) target.Play();
                    else if (pendingWasPaused) target.Pause();
                    hasPendingSwitch = false;
                }, reprepare: true);
            }
            else
            {
                SubmitOrDefer(() =>
                {
                    target.Play();
                    hasPendingSwitch = false;
                }, reprepare: true);
            }
        }
    }

    [Serializable]
    public sealed class VideoPlayerPlayTriggerOutput : VideoPlayerOutputBase, ITriggerOutput
    {
        public void Initialize(RouteContext context) => InitializePendingOperation(context, VideoPreparationLane.Trigger);
        public void Invoke() => SubmitOrDefer(() => target.Play());
    }

    [Serializable]
    public sealed class VideoPlayerPauseTriggerOutput : VideoPlayerOutputBase, ITriggerOutput
    {
        public void Initialize(RouteContext context) => InitializePendingOperation(context, VideoPreparationLane.Trigger);
        public void Invoke() => SubmitOrDefer(() => target.Pause());
    }

    [Serializable]
    public sealed class VideoPlayerStopTriggerOutput : VideoPlayerOutputBase, ITriggerOutput
    {
        public void Initialize(RouteContext context) => InitializePendingOperation(context, VideoPreparationLane.Trigger);
        public void Invoke() => SubmitOrDefer(() => target.Stop());
    }

    [Serializable]
    public sealed class VideoPlayerRestartTriggerOutput : VideoPlayerOutputBase, ITriggerOutput
    {
        public void Initialize(RouteContext context) => InitializePendingOperation(context, VideoPreparationLane.Trigger);
        public void Invoke() => SubmitOrDefer(() => target.Play(), reprepare: true);
    }
}
