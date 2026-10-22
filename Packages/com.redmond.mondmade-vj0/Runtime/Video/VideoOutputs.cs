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

        public static double ClampTime(VideoPlayer player, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            var length = player.length;
            return Math.Max(0d, length > 0d ? Math.Min(value, length) : value);
        }
    }

    [Serializable]
    public sealed class VideoPlayerSpeedFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private VideoPlayer target;
        public void Initialize(RouteContext context) => VideoOutputHelpers.Require(target);
        public void Write(float value) => target.playbackSpeed = value;
    }

    [Serializable]
    public sealed class VideoPlayerPlayBoolOutput : RouteNode, IValueOutput<bool>
    {
        [SerializeField] private VideoPlayer target;
        public void Initialize(RouteContext context) => VideoOutputHelpers.Require(target);
        public void Write(bool value) { if (value) target.Play(); else target.Pause(); }
    }

    [Serializable]
    public sealed class VideoPlayerTimeFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private VideoPlayer target;
        [NonSerialized] private bool hasPending;
        [NonSerialized] private double pendingTime;
        public void Initialize(RouteContext context)
        {
            VideoOutputHelpers.Require(target);
            target.prepareCompleted += OnPrepared;
            context.Scope.Add(() => { if (target != null) target.prepareCompleted -= OnPrepared; });
        }
        public void Write(float value)
        {
            var time = VideoOutputHelpers.ClampTime(target, value);
            if (target.isPrepared) target.time = time;
            else
            {
                pendingTime = time;
                hasPending = true;
                if (!target.isPlaying && !target.isPrepared && !target.isLooping) target.Prepare();
            }
        }
        private void OnPrepared(VideoPlayer player)
        {
            if (!hasPending) return;
            player.time = VideoOutputHelpers.ClampTime(player, pendingTime);
            hasPending = false;
        }
    }

    [Serializable]
    public sealed class VideoPlayerNormalizedTimeFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private VideoPlayer target;
        [NonSerialized] private bool hasPending;
        [NonSerialized] private float pendingNormalized;
        public void Initialize(RouteContext context)
        {
            VideoOutputHelpers.Require(target);
            target.prepareCompleted += OnPrepared;
            context.Scope.Add(() => { if (target != null) target.prepareCompleted -= OnPrepared; });
        }
        public void Write(float value)
        {
            var normalized = Mathf.Clamp01(value);
            if (target.isPrepared) target.time = target.length * normalized;
            else
            {
                pendingNormalized = normalized;
                hasPending = true;
                if (!target.isPlaying && !target.isPrepared && !target.isLooping) target.Prepare();
            }
        }
        private void OnPrepared(VideoPlayer player)
        {
            if (!hasPending) return;
            player.time = player.length * pendingNormalized;
            hasPending = false;
        }
    }

    [Serializable]
    public sealed class VideoPlayerClipIndexIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private VideoPlayer target;
        [SerializeField] private VideoClip[] clips = Array.Empty<VideoClip>();
        [SerializeField] private VideoClipSwitchMode switchMode = VideoClipSwitchMode.KeepState;
        [NonSerialized] private int currentIndex = -1;
        public void Initialize(RouteContext context)
        {
            VideoOutputHelpers.Require(target);
            if (clips == null || clips.Length == 0) throw new InvalidOperationException("VideoPlayer clip list is empty.");
            for (var i = 0; i < clips.Length; i++) if (clips[i] == null) throw new InvalidOperationException($"VideoPlayer clip {i} is not assigned.");
        }
        public void Write(int value)
        {
            var index = Mathf.Clamp(value, 0, clips.Length - 1);
            if (index == currentIndex && target.clip == clips[index]) return;
            var wasPlaying = target.isPlaying;
            var previousTime = target.time;
            currentIndex = index;
            target.clip = clips[index];
            if (switchMode == VideoClipSwitchMode.Restart)
            {
                target.time = 0d;
                target.Play();
            }
            else if (switchMode == VideoClipSwitchMode.AutoPlay || wasPlaying)
            {
                target.time = switchMode == VideoClipSwitchMode.KeepState
                    ? VideoOutputHelpers.ClampTime(target, previousTime)
                    : 0d;
                target.Play();
            }
        }
    }

    [Serializable]
    public sealed class VideoPlayerPlayTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private VideoPlayer target;
        public void Initialize(RouteContext context) => VideoOutputHelpers.Require(target);
        public void Invoke() => target.Play();
    }

    [Serializable]
    public sealed class VideoPlayerPauseTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private VideoPlayer target;
        public void Initialize(RouteContext context) => VideoOutputHelpers.Require(target);
        public void Invoke() => target.Pause();
    }

    [Serializable]
    public sealed class VideoPlayerStopTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private VideoPlayer target;
        public void Initialize(RouteContext context) => VideoOutputHelpers.Require(target);
        public void Invoke() => target.Stop();
    }

    [Serializable]
    public sealed class VideoPlayerRestartTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private VideoPlayer target;
        public void Initialize(RouteContext context) => VideoOutputHelpers.Require(target);
        public void Invoke()
        {
            if (target.isPrepared) { target.time = 0d; target.Play(); }
            else { target.Prepare(); target.Play(); }
        }
    }
}
