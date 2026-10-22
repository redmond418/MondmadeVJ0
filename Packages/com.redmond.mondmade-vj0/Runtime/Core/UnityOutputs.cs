using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MondmadeVJ0
{
    public static class ComponentAccessorRegistry
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly Type TargetType;
            public readonly Type ValueType;
            public readonly string Member;
            public Key(Type targetType, Type valueType, string member) { TargetType = targetType; ValueType = valueType; Member = member; }
            public bool Equals(Key other) => TargetType == other.TargetType && ValueType == other.ValueType && string.Equals(Member, other.Member, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => ((TargetType?.GetHashCode() ?? 0) * 397 ^ (ValueType?.GetHashCode() ?? 0)) * 397 ^ (Member?.GetHashCode() ?? 0);
        }

        private static readonly Dictionary<Key, Delegate> Setters = new Dictionary<Key, Delegate>();
        public static void Register<T>(Type targetType, string memberName, Action<Component, T> setter)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (string.IsNullOrWhiteSpace(memberName)) throw new ArgumentException("Member name is required.", nameof(memberName));
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            Setters[new Key(targetType, typeof(T), memberName)] = setter;
        }

        public static bool TryGet<T>(Type targetType, string memberName, out Action<Component, T> setter)
        {
            if (Setters.TryGetValue(new Key(targetType, typeof(T), memberName), out var registered))
            {
                setter = (Action<Component, T>)registered;
                return true;
            }
            setter = null;
            return false;
        }
    }

    public enum AudioClipSwitchMode { KeepTime, AutoPlay, Restart }

    [Serializable]
    public sealed class AudioSourceVolumeFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => Require(target);
        public void Write(float value) => target.volume = Mathf.Clamp01(value);
        internal static void Require(UnityEngine.Object target) { if (target == null) throw new InvalidOperationException("AudioSource target is not assigned."); }
    }

    [Serializable]
    public sealed class AudioSourcePitchFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Write(float value) => target.pitch = value;
    }

    [Serializable]
    public sealed class AudioSourcePanFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Write(float value) => target.panStereo = Mathf.Clamp(value, -1f, 1f);
    }

    [Serializable]
    public sealed class AudioSourcePlayBoolOutput : RouteNode, IValueOutput<bool>
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Write(bool value) { if (value) target.Play(); else target.Pause(); }
    }

    [Serializable]
    public sealed class AudioSourceTimeFloatOutput : RouteNode, IValueOutput<float>
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Write(float value)
        {
            if (target.clip == null) return;
            target.time = Mathf.Clamp(value, 0f, Mathf.Max(0f, target.clip.length));
        }
    }

    [Serializable]
    public sealed class AudioSourceClipIndexIntOutput : RouteNode, IValueOutput<int>
    {
        [SerializeField] private AudioSource target;
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClipSwitchMode switchMode = AudioClipSwitchMode.KeepTime;
        private int currentIndex = -1;
        public void Initialize(RouteContext context)
        {
            AudioSourceVolumeFloatOutput.Require(target);
            if (clips == null || clips.Length == 0) throw new InvalidOperationException("AudioSource clip list is empty.");
            for (var i = 0; i < clips.Length; i++) if (clips[i] == null) throw new InvalidOperationException($"AudioSource clip {i} is not assigned.");
        }
        public void Write(int value)
        {
            var index = Mathf.Clamp(value, 0, clips.Length - 1);
            if (index == currentIndex && target.clip == clips[index]) return;
            var wasPlaying = target.isPlaying;
            var oldTime = target.time;
            currentIndex = index;
            target.clip = clips[index];
            switch (switchMode)
            {
                case AudioClipSwitchMode.KeepTime:
                    target.time = Mathf.Clamp(oldTime, 0f, Mathf.Max(0f, clips[index].length));
                    break;
                case AudioClipSwitchMode.Restart:
                    target.time = 0f;
                    break;
            }
            if (switchMode == AudioClipSwitchMode.AutoPlay || wasPlaying) target.Play();
        }
    }

    [Serializable]
    public sealed class AudioSourcePlayTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Invoke() => target.Play();
    }

    [Serializable]
    public sealed class AudioSourcePauseTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Invoke() => target.Pause();
    }

    [Serializable]
    public sealed class AudioSourceStopTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Invoke() => target.Stop();
    }

    [Serializable]
    public sealed class AudioSourceRestartTriggerOutput : RouteNode, ITriggerOutput
    {
        [SerializeField] private AudioSource target;
        public void Initialize(RouteContext context) => AudioSourceVolumeFloatOutput.Require(target);
        public void Invoke() { target.Stop(); target.time = 0f; target.Play(); }
    }

    [Serializable]
    public abstract class ComponentMemberOutput<T> : RouteNode, IValueOutput<T>
    {
        [SerializeField] private Component target;
        [SerializeField] private string memberName;
        [NonSerialized] private FieldInfo field;
        [NonSerialized] private Action<T> propertySetter;
        [NonSerialized] private Action<Component, T> generatedSetter;
        public Component Target => target;
        public string MemberName => memberName;
        public Type ValueType => typeof(T);

        public void Initialize(RouteContext context)
        {
            if (target == null) throw new InvalidOperationException("Generic component target is not assigned.");
            if (string.IsNullOrWhiteSpace(memberName)) throw new InvalidOperationException("Generic component member name is empty.");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var type = target.GetType();
            if (ComponentAccessorRegistry.TryGet<T>(type, memberName, out generatedSetter)) return;
            field = type.GetField(memberName, flags);
            if (field != null)
            {
                if (field.FieldType != typeof(T) || field.IsInitOnly || field.IsLiteral)
                    throw new InvalidOperationException($"Public field '{type.Name}.{memberName}' is not a writable {typeof(T).Name}.");
                return;
            }
            var property = type.GetProperty(memberName, flags);
            var setter = property?.GetSetMethod(nonPublic: false);
            if (property == null || property.PropertyType != typeof(T) || setter == null)
                throw new InvalidOperationException($"'{type.Name}.{memberName}' is not a public writable {typeof(T)} field or property.");
            propertySetter = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, setter);
        }

        public void Write(T value)
        {
            if (generatedSetter != null) { generatedSetter(target, value); return; }
            if (field != null) field.SetValue(target, value);
            else propertySetter(value);
        }
    }

    [Serializable] public sealed class ComponentMemberFloatOutput : ComponentMemberOutput<float> { }
    [Serializable] public sealed class ComponentMemberIntOutput : ComponentMemberOutput<int> { }
    [Serializable] public sealed class ComponentMemberBoolOutput : ComponentMemberOutput<bool> { }
    [Serializable] public sealed class ComponentMemberVector2Output : ComponentMemberOutput<Vector2> { }
    [Serializable] public sealed class ComponentMemberVector3Output : ComponentMemberOutput<Vector3> { }
    [Serializable] public sealed class ComponentMemberVector4Output : ComponentMemberOutput<Vector4> { }
    [Serializable] public sealed class ComponentMemberColorOutput : ComponentMemberOutput<Color> { }
}
