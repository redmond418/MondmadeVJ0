using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MondmadeVJ0.Tests
{
    public sealed class AudioSourceOutputPlayModeTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.Destroy(cleanup[i]);
            cleanup.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AudioSourcePlaybackAndClipOutputsApplyTransportCommands()
        {
            var target = Own(new GameObject("Audio Output PlayMode Target"));
            var source = target.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;

            var firstClip = Own(AudioClip.Create("Audio Output PlayMode Clip 0", 44100, 1, 44100, false));
            var secondClip = Own(AudioClip.Create("Audio Output PlayMode Clip 1", 44100, 1, 44100, false));
            source.clip = firstClip;

            var playBool = Configure(new AudioSourcePlayBoolOutput(), source);
            playBool.Write(true);
            yield return null;
            Assert.That(source.isPlaying, Is.True);

            source.time = 0.25f;
            playBool.Write(false);
            yield return null;
            Assert.That(source.isPlaying, Is.False);
            Assert.That(source.time, Is.EqualTo(0.25f).Within(0.08f));

            playBool.Write(true);
            yield return null;
            Assert.That(source.isPlaying, Is.True);

            var pause = Configure(new AudioSourcePauseTriggerOutput(), source);
            pause.Invoke();
            yield return null;
            Assert.That(source.isPlaying, Is.False);

            var play = Configure(new AudioSourcePlayTriggerOutput(), source);
            play.Invoke();
            yield return null;
            Assert.That(source.isPlaying, Is.True);

            source.time = 0.4f;
            var stop = Configure(new AudioSourceStopTriggerOutput(), source);
            stop.Invoke();
            yield return null;
            Assert.That(source.isPlaying, Is.False);
            Assert.That(source.time, Is.EqualTo(0f).Within(0.02f));

            play.Invoke();
            yield return null;
            source.time = 0.4f;
            var restart = Configure(new AudioSourceRestartTriggerOutput(), source);
            restart.Invoke();
            yield return null;
            Assert.That(source.isPlaying, Is.True);
            Assert.That(source.time, Is.LessThan(0.1f));

            var clipIndex = new AudioSourceClipIndexIntOutput();
            SetField(clipIndex, "target", source);
            SetField(clipIndex, "clips", new[] { firstClip, secondClip });
            clipIndex.Initialize(null);

            source.time = 0.3f;
            SetField(clipIndex, "switchMode", AudioClipSwitchMode.KeepTime);
            clipIndex.Write(1);
            yield return null;
            Assert.That(source.clip, Is.SameAs(secondClip));
            Assert.That(source.isPlaying, Is.True);
            Assert.That(source.time, Is.EqualTo(0.3f).Within(0.08f));

            stop.Invoke();
            yield return null;
            SetField(clipIndex, "switchMode", AudioClipSwitchMode.AutoPlay);
            clipIndex.Write(0);
            yield return null;
            Assert.That(source.clip, Is.SameAs(firstClip));
            Assert.That(source.isPlaying, Is.True);

            stop.Invoke();
            yield return null;
            SetField(clipIndex, "switchMode", AudioClipSwitchMode.Restart);
            clipIndex.Write(1);
            yield return null;
            Assert.That(source.clip, Is.SameAs(secondClip));
            Assert.That(source.isPlaying, Is.False);
            Assert.That(source.time, Is.EqualTo(0f).Within(0.02f));
        }

        private static T Configure<T>(T output, AudioSource target)
        {
            SetField(output, "target", target);
            output.GetType().GetMethod("Initialize").Invoke(output, new object[] { null });
            return output;
        }

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        private T Own<T>(T value) where T : Object
        {
            cleanup.Add(value);
            return value;
        }
    }
}
