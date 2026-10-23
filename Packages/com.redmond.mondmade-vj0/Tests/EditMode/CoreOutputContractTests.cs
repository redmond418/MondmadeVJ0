using System;
using System.Collections.Generic;
using System.Reflection;
using MondmadeVJ0;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace MondmadeVJ0.Tests
{
    public sealed class CoreOutputProbe : MonoBehaviour
    {
        public float FloatValue;
        public int IntValue { get; set; }
        public bool BoolValue;
        public Vector2 Vector2Value;
        public Vector3 Vector3Value;
        public Vector4 Vector4Value;
        public Color ColorValue;
    }

    public sealed class CoreOutputContractTests
    {
        private readonly List<UnityEngine.Object> cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (var i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) UnityEngine.Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test]
        public void ActiveOutputsAndObjectGroupWriteAndClampTheirTargets()
        {
            var activeTarget = Own(new GameObject("Active Output Target"));
            var active = new GameObjectActiveBoolOutput();
            SetField(active, "target", activeTarget);
            active.Initialize(null);
            active.Write(false);
            Assert.That(activeTarget.activeSelf, Is.False);
            active.Write(true);
            Assert.That(activeTarget.activeSelf, Is.True);

            var behaviour = activeTarget.AddComponent<AudioSource>();
            var behaviourOutput = new BehaviourEnabledBoolOutput();
            SetField(behaviourOutput, "target", behaviour);
            behaviourOutput.Initialize(null);
            behaviourOutput.Write(false);
            Assert.That(behaviour.enabled, Is.False);

            var renderer = activeTarget.AddComponent<MeshRenderer>();
            var rendererOutput = new RendererEnabledBoolOutput();
            SetField(rendererOutput, "target", renderer);
            rendererOutput.Initialize(null);
            rendererOutput.Write(false);
            Assert.That(renderer.enabled, Is.False);

            var first = Own(new GameObject("Group 0"));
            var second = Own(new GameObject("Group 1"));
            var group = new ObjectGroupIndexIntOutput();
            SetField(group, "objects", new[] { first, null, second });
            group.Initialize(null);
            group.Write(99);
            Assert.That(first.activeSelf, Is.False);
            Assert.That(second.activeSelf, Is.True);
            group.Write(-1);
            Assert.That(first.activeSelf, Is.True);
            Assert.That(second.activeSelf, Is.False);
        }

        [Test]
        public void TransformVectorAndAxisOutputsRespectSpaceAndPreserveOtherAxes()
        {
            var parentObject = Own(new GameObject("Transform Output Parent"));
            parentObject.transform.position = new Vector3(10f, 0f, 0f);
            var targetObject = Own(new GameObject("Transform Output Target"));
            targetObject.transform.SetParent(parentObject.transform, false);
            var target = targetObject.transform;

            var position = new TransformPositionVector3Output();
            SetField(position, "target", target);
            position.Initialize(null);
            position.Write(new Vector3(1f, 2f, 3f));
            Assert.That(target.localPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            SetField(position, "worldSpace", true);
            position.Write(new Vector3(20f, 4f, 6f));
            Assert.That(target.position, Is.EqualTo(new Vector3(20f, 4f, 6f)));

            var rotation = new TransformRotationVector3Output();
            SetField(rotation, "target", target);
            rotation.Initialize(null);
            rotation.Write(new Vector3(15f, 25f, 35f));
            Assert.That(Mathf.Abs(target.localEulerAngles.x - 15f), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(target.localEulerAngles.y - 25f), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(target.localEulerAngles.z - 35f), Is.LessThan(0.01f));

            var scale = new TransformScaleVector3Output();
            SetField(scale, "target", target);
            scale.Initialize(null);
            scale.Write(new Vector3(2f, 3f, 4f));
            Assert.That(target.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));

            target.localPosition = new Vector3(1f, 2f, 3f);
            WriteAxis(new TransformPositionAxisFloatOutput(), target, TransformAxis.Y, 8f);
            Assert.That(target.localPosition, Is.EqualTo(new Vector3(1f, 8f, 3f)));
            target.localEulerAngles = new Vector3(5f, 6f, 7f);
            WriteAxis(new TransformRotationAxisFloatOutput(), target, TransformAxis.Z, 45f);
            Assert.That(target.localEulerAngles.z, Is.EqualTo(45f).Within(0.01f));
            target.localScale = new Vector3(1f, 1f, 1f);
            WriteAxis(new TransformScaleAxisFloatOutput(), target, TransformAxis.X, 2.5f);
            Assert.That(target.localScale, Is.EqualTo(new Vector3(2.5f, 1f, 1f)));
        }

        [Test]
        public void AudioSourceValueOutputsClampScalarsAndSwitchConfiguredClips()
        {
            var targetObject = Own(new GameObject("Audio Output Target"));
            var source = targetObject.AddComponent<AudioSource>();
            var firstClip = CreateClip("Audio Output Clip 0", 44100);
            var secondClip = CreateClip("Audio Output Clip 1", 22050);
            source.clip = firstClip;

            var volume = new AudioSourceVolumeFloatOutput();
            SetField(volume, "target", source);
            volume.Initialize(null);
            volume.Write(1.5f);
            Assert.That(source.volume, Is.EqualTo(1f));

            var pitch = new AudioSourcePitchFloatOutput();
            SetField(pitch, "target", source);
            pitch.Initialize(null);
            pitch.Write(1.25f);
            Assert.That(source.pitch, Is.EqualTo(1.25f));

            var pan = new AudioSourcePanFloatOutput();
            SetField(pan, "target", source);
            pan.Initialize(null);
            pan.Write(-2f);
            Assert.That(source.panStereo, Is.EqualTo(-1f));

            var time = new AudioSourceTimeFloatOutput();
            SetField(time, "target", source);
            time.Initialize(null);
            time.Write(2f);
            Assert.That(source.time, Is.LessThanOrEqualTo(firstClip.length));

            var clipIndex = new AudioSourceClipIndexIntOutput();
            SetField(clipIndex, "target", source);
            SetField(clipIndex, "clips", new[] { firstClip, secondClip });
            SetField(clipIndex, "switchMode", AudioClipSwitchMode.Restart);
            clipIndex.Initialize(null);
            clipIndex.Write(1);
            Assert.That(source.clip, Is.SameAs(secondClip));
            Assert.That(source.time, Is.EqualTo(0f).Within(0.01f));
            clipIndex.Write(-1);
            Assert.That(source.clip, Is.SameAs(firstClip));
        }

        [Test]
        public void GenericComponentOutputsWriteEverySupportedValueType()
        {
            var targetObject = Own(new GameObject("Generic Output Target"));
            var target = targetObject.AddComponent<CoreOutputProbe>();

            WriteMember(new ComponentMemberFloatOutput(), target, "FloatValue", 1.25f);
            WriteMember(new ComponentMemberIntOutput(), target, "IntValue", 17);
            WriteMember(new ComponentMemberBoolOutput(), target, "BoolValue", true);
            WriteMember(new ComponentMemberVector2Output(), target, "Vector2Value", new Vector2(1f, 2f));
            WriteMember(new ComponentMemberVector3Output(), target, "Vector3Value", new Vector3(3f, 4f, 5f));
            WriteMember(new ComponentMemberVector4Output(), target, "Vector4Value", new Vector4(6f, 7f, 8f, 9f));
            WriteMember(new ComponentMemberColorOutput(), target, "ColorValue", new Color(0.1f, 0.2f, 0.3f, 0.4f));

            Assert.That(target.FloatValue, Is.EqualTo(1.25f));
            Assert.That(target.IntValue, Is.EqualTo(17));
            Assert.That(target.BoolValue, Is.True);
            Assert.That(target.Vector2Value, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(target.Vector3Value, Is.EqualTo(new Vector3(3f, 4f, 5f)));
            Assert.That(target.Vector4Value, Is.EqualTo(new Vector4(6f, 7f, 8f, 9f)));
            Assert.That(target.ColorValue, Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)));
        }

        [Test]
        public void TypedSignalOutputsPublishValuesAndTriggerExactlyOnce()
        {
            var signalObject = Own(new GameObject("Typed Signal Output Target"));
            WriteSignal(new SignalFloatOutput(), signalObject.AddComponent<SharedFloatSignal>(), 0.75f);
            WriteSignal(new SignalIntOutput(), signalObject.AddComponent<SharedIntSignal>(), 42);
            WriteSignal(new SignalBoolOutput(), signalObject.AddComponent<SharedBoolSignal>(), true);
            WriteSignal(new SignalVector2Output(), signalObject.AddComponent<SharedVector2Signal>(), new Vector2(1f, 2f));
            WriteSignal(new SignalVector3Output(), signalObject.AddComponent<SharedVector3Signal>(), new Vector3(3f, 4f, 5f));
            WriteSignal(new SignalVector4Output(), signalObject.AddComponent<SharedVector4Signal>(), new Vector4(6f, 7f, 8f, 9f));
            WriteSignal(new SignalColorOutput(), signalObject.AddComponent<SharedColorSignal>(), new Color(0.1f, 0.2f, 0.3f, 0.4f));

            var trigger = signalObject.AddComponent<SharedTriggerSignal>();
            var output = new SignalTriggerOutput();
            SetField(output, "signal", trigger);
            var observed = 0;
            using (trigger.Stream.Subscribe(_ => observed++))
            {
                output.Initialize(null);
                output.Invoke();
                Assert.That(observed, Is.EqualTo(1));
            }
        }

        [Test]
        public void RequiredTargetsAndGroupsRejectMissingConfiguration()
        {
            Assert.Throws<InvalidOperationException>(() => new GameObjectActiveBoolOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new BehaviourEnabledBoolOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new RendererEnabledBoolOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new TransformPositionVector3Output().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new TransformRotationVector3Output().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new TransformScaleVector3Output().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new TransformPositionAxisFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new TransformRotationAxisFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new TransformScaleAxisFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new ObjectGroupIndexIntOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new AudioSourceVolumeFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new ComponentMemberFloatOutput().Initialize(null));
            Assert.Throws<InvalidOperationException>(() => new ComponentMemberIntOutput().Initialize(null));
        }

        private void WriteAxis(TransformAxisFloatOutput output, Transform target, TransformAxis axis, float value)
        {
            SetField(output, "target", target);
            SetField(output, "axis", axis);
            output.Initialize(null);
            output.Write(value);
        }

        private static void WriteMember<T>(ComponentMemberOutput<T> output, Component target, string member, T value)
        {
            SetField(output, "target", target);
            SetField(output, "memberName", member);
            output.Initialize(null);
            output.Write(value);
        }

        private static void WriteSignal<T>(IValueOutput<T> output, SharedValueSignal<T> signal, T value)
        {
            SetField(output, "signal", signal);
            output.Initialize(null);
            output.Write(value);
            Assert.That(signal.Value, Is.EqualTo(value));
        }

        private AudioClip CreateClip(string clipName, int samples)
        {
            var clip = Own(AudioClip.Create(clipName, samples, 1, 44100, false));
            return clip;
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            cleanup.Add(value);
            return value;
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
    }
}
